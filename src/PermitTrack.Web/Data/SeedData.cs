using System.Text;
using Microsoft.EntityFrameworkCore;
using PermitTrack.Web.Domain;

namespace PermitTrack.Web.Data;

/// <summary>
/// Fictional demo data. Dates are relative to "now" so the dashboard always shows a realistic mix of
/// overdue reviews, today's inspections and permits nearing expiration.
/// </summary>
public static class SeedData
{
    /// <summary>Changes whenever the seed data or schema changes, so old demo databases get rebuilt.</summary>
    private const string SeedMarker = "@v2.demoville.example";

    /// <summary>True if the database has the current schema and either no data yet or the current seed.</summary>
    public static bool IsCurrent(AppDbContext db)
    {
        // Look the table up in SQLite's catalog rather than querying it, so an old database doesn't log errors.
        var hasSettings = db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'Settings'")
            .Single() > 0;
        return hasSettings && (!db.Users.Any() || db.Users.Any(u => u.Email.EndsWith(SeedMarker)));
    }

    public static void Initialize(AppDbContext db, DateTime now, string uploadDirectory)
    {
        if (db.Users.Any())
            return;

        var alex = new AppUser { Name = "Alex Example", Email = "alex" + SeedMarker, Role = UserRole.Applicant, Company = "Example Builders LLC" };
        var pat = new AppUser { Name = "Pat Placeholder", Email = "pat" + SeedMarker, Role = UserRole.Applicant, Company = "Homeowner" };
        var morgan = new AppUser { Name = "Morgan Mockup", Email = "morgan" + SeedMarker, Role = UserRole.Clerk, Company = "Demoville Permit Office" };
        var dana = new AppUser { Name = "Dana Dummy", Email = "dana" + SeedMarker, Role = UserRole.Inspector, Company = "Demoville Inspections" };
        var lee = new AppUser { Name = "Lee Testwell", Email = "lee" + SeedMarker, Role = UserRole.Inspector, Company = "Demoville Inspections" };
        var avery = new AppUser { Name = "Avery Admin", Email = "avery" + SeedMarker, Role = UserRole.Admin, Company = "Demoville Permit Office" };
        db.Users.AddRange(alex, pat, morgan, dana, lee, avery);
        db.SaveChanges();

        var samplePlan = WriteSamplePdf(uploadDirectory);
        var year = now.Year;
        var counters = new Dictionary<PermitType, int>();

        Permit New(PermitType type, AppUser applicant, string address, string description, decimal valuation,
            int createdDaysAgo, string? contractor = null, string? license = null)
        {
            counters[type] = counters.GetValueOrDefault(type) + 1;
            var created = now.Date.AddDays(-createdDaysAgo).AddHours(9);
            var p = new Permit
            {
                Number = $"{type.ToString()[0]}-{year}-{counters[type]:D4}",
                Type = type,
                Address = address,
                Description = description,
                Valuation = valuation,
                ContractorName = contractor,
                ContractorLicense = license,
                FeeTotal = FeeCalculator.Calculate(type, valuation).Total,
                ApplicantId = applicant.Id,
                CreatedAt = created
            };
            p.Events.Add(new PermitEvent { At = created, UserId = applicant.Id, Action = "Application started" });
            if (type != PermitType.Demolition)
            {
                p.Documents.Add(new PermitDocument
                {
                    FileName = type == PermitType.Building ? "site-plan.pdf" : "scope-of-work.pdf",
                    StoredName = samplePlan, ContentType = "application/pdf", SizeBytes = new FileInfo(Path.Combine(uploadDirectory, samplePlan)).Length,
                    UploadedAt = created.AddMinutes(20), UploadedById = applicant.Id
                });
                p.Events.Add(new PermitEvent { At = created.AddMinutes(20), UserId = applicant.Id, Action = "Document uploaded", Note = p.Documents[0].FileName });
            }
            db.Permits.Add(p);
            return p;
        }

        void Move(Permit p, AppUser by, PermitStatus to, int daysAgo, string? note = null)
        {
            var at = now.Date.AddDays(-daysAgo).AddHours(10).AddMinutes(p.Events.Count * 7);
            p.Events.Add(new PermitEvent { At = at, UserId = by.Id, Action = $"{Ui.Label(p.Status)} → {Ui.Label(to)}", Note = note });
            p.Status = to;
            switch (to)
            {
                case PermitStatus.Submitted: p.SubmittedAt = at; break;
                case PermitStatus.UnderReview: p.ReviewerId = by.Id; break;
                case PermitStatus.Issued: p.IssuedAt = at; break;
                case PermitStatus.Finaled: p.FinaledAt = at; break;
            }
        }

        void Pay(Permit p, int daysAgo, string reference)
        {
            p.FeePaidAt = now.Date.AddDays(-daysAgo).AddHours(11);
            p.PaymentReference = reference;
            p.Events.Add(new PermitEvent { At = p.FeePaidAt.Value, UserId = morgan.Id, Action = $"Payment of {p.FeeTotal:C} recorded", Note = $"Reference {reference}" });
        }

        void Issue(Permit p, int submittedDaysAgo, int issuedDaysAgo, string receipt)
        {
            Move(p, p.ApplicantId == alex.Id ? alex : pat, PermitStatus.Submitted, submittedDaysAgo);
            Move(p, morgan, PermitStatus.UnderReview, submittedDaysAgo - 1);
            Pay(p, issuedDaysAgo, receipt);
            Move(p, morgan, PermitStatus.Issued, issuedDaysAgo, "Plans reviewed and approved.");
        }

        void Inspect(Permit p, InspectionType type, AppUser inspector, int dayOffset, InspectionResult result = InspectionResult.Scheduled, string? notes = null)
        {
            var date = Weekday(now, dayOffset);
            var inspection = new Inspection { Type = type, ScheduledFor = date, InspectorId = inspector.Id, Result = result, Notes = notes };
            p.Events.Add(new PermitEvent { At = date.ToDateTime(new TimeOnly(8, 0)).AddDays(-2), UserId = p.ApplicantId, Action = $"{Ui.Label(type)} inspection scheduled", Note = $"{date:ddd, MMM d} — assigned to {inspector.Name}" });
            if (result != InspectionResult.Scheduled)
            {
                inspection.CompletedAt = date.ToDateTime(new TimeOnly(13, 30));
                inspection.Latitude = 32.47 + Random.Shared.NextDouble() * 0.04;
                inspection.Longitude = -93.77 + Random.Shared.NextDouble() * 0.04;
                p.Events.Add(new PermitEvent { At = inspection.CompletedAt.Value, UserId = inspector.Id, Action = $"{Ui.Label(type)} inspection {Ui.Label(result).ToLowerInvariant()}", Note = notes });
            }
            p.Inspections.Add(inspection);
        }

        // Active new-home build: two inspections passed, insulation coming up.
        var home = New(PermitType.Building, alex, "123 Example Ln, Demoville, ZZ 00000",
            "New 2,450 sq ft single-family residence with attached two-car garage.", 285_000m, 52, "Example Builders LLC", "DEMO-RB-0001");
        Issue(home, 50, 40, "RCPT-10482");
        Inspect(home, InspectionType.Footing, dana, -30, InspectionResult.Passed, "Footing depth and rebar placement per plan.");
        Inspect(home, InspectionType.Framing, lee, -12, InspectionResult.Passed, "Framing, fireblocking and hurricane straps OK.");
        Inspect(home, InspectionType.Insulation, dana, 1);

        // Failed rough-in, waiting on the applicant to re-request.
        var panel = New(PermitType.Electrical, pat, "456 Placeholder Pl, Demoville, ZZ 00000",
            "Upgrade service panel from 100A to 200A; add two 20A kitchen circuits.", 4_800m, 14, "Sparky Sample Electric", "DEMO-EL-0005");
        Issue(panel, 13, 9, "RCPT-10531");
        Inspect(panel, InspectionType.RoughElectrical, lee, -3, InspectionResult.Failed,
            "Missing bonding jumper at water heater. Garage receptacles lack GFCI protection. Correct and request re-inspection.");

        // Completed job.
        var heater = New(PermitType.Plumbing, alex, "789 Lorem Ipsum Way, Demoville, ZZ 00000",
            "Replace 50-gal gas water heater; new expansion tank and T&P discharge line.", 2_200m, 30, "Example Builders LLC", "DEMO-PL-0002");
        Issue(heater, 29, 27, "RCPT-10455");
        Inspect(heater, InspectionType.RoughPlumbing, dana, -20, InspectionResult.Passed);
        Inspect(heater, InspectionType.FinalPlumbing, dana, -5, InspectionResult.Passed, "Venting and T&P discharge correct. Final approved.");
        Move(heater, dana, PermitStatus.Finaled, 5, "All required inspections passed. Certificate of completion issued.");
        heater.FinaledAt = heater.Inspections[^1].CompletedAt;

        var deck = New(PermitType.Building, pat, "42 Sample St, Demoville, ZZ 00000",
            "Attached 16x20 wood deck with stairs, 30 in. above grade.", 18_500m, 6);
        Move(deck, pat, PermitStatus.Submitted, 4);
        Pay(deck, 4, "RCPT-10560");
        Move(deck, morgan, PermitStatus.UnderReview, 2);

        // Overdue for review (submitted 10 days ago, target is 7).
        var tenant = New(PermitType.Building, alex, "1000 Mockup Blvd, Suite 101, Demoville, ZZ 00000",
            "Commercial tenant finish-out for dental office: new partitions, ceilings, lighting.", 142_000m, 11, "Example Builders LLC", "DEMO-CB-0003");
        Move(tenant, alex, PermitStatus.Submitted, 10);

        var hvac = New(PermitType.Mechanical, alex, "17 Sandbox Dr, Demoville, ZZ 00000",
            "Replace 4-ton split HVAC system and air handler; reuse existing ductwork.", 9_800m, 3, "Example Builders LLC", "DEMO-MC-0004");
        Move(hvac, alex, PermitStatus.Submitted, 2);

        var garage = New(PermitType.Building, pat, "314 Prototype Pkwy, Demoville, ZZ 00000",
            "Convert attached garage to bedroom and bath.", 32_000m, 16);
        Move(garage, pat, PermitStatus.Submitted, 15);
        Pay(garage, 15, "RCPT-10512");
        Move(garage, morgan, PermitStatus.UnderReview, 13);
        Move(garage, morgan, PermitStatus.CorrectionsRequired, 11,
            "Provide egress window dimensions for the new bedroom and a smoke/CO alarm layout.");

        New(PermitType.Demolition, alex, "8 Dummy Rd, Demoville, ZZ 00000", "Demolish 10x12 detached storage shed; slab to remain.", 0m, 1);
        New(PermitType.Electrical, pat, "99 Test Ct, Demoville, ZZ 00000", "New 50A circuit for Level 2 EV charger in garage.", 1_900m, 0);

        // Issued almost six months ago with little activity: expires soon.
        var patio = New(PermitType.Building, alex, "2468 Fictional Ave, Demoville, ZZ 00000",
            "Covered patio addition, 14x24, attached to rear of house.", 26_000m, 180, "Example Builders LLC", "DEMO-RB-0001");
        Issue(patio, 179, 176, "RCPT-09870");
        Inspect(patio, InspectionType.Footing, lee, -172, InspectionResult.Passed);

        var bath = New(PermitType.Plumbing, alex, "1357 Widget Way, Demoville, ZZ 00000",
            "Primary bathroom remodel: relocate shower drain, add second lavatory.", 7_600m, 18, "Example Builders LLC", "DEMO-PL-0002");
        Issue(bath, 17, 15, "RCPT-10502");
        Inspect(bath, InspectionType.RoughPlumbing, lee, 0);

        var pool = New(PermitType.Building, pat, "404 Notfound Ln, Demoville, ZZ 00000",
            "In-ground fiberglass pool, 14x28, with equipment pad.", 48_000m, 32);
        Move(pool, pat, PermitStatus.Submitted, 30);
        Move(pool, morgan, PermitStatus.UnderReview, 28);
        Move(pool, morgan, PermitStatus.Rejected, 25, "Proposed pool lies within a platted drainage servitude. Relocate outside the servitude and reapply.");

        var hood = New(PermitType.Mechanical, alex, "500 Pixel St, Demoville, ZZ 00000",
            "Type I commercial kitchen hood and exhaust fan for restaurant.", 38_000m, 10, "Example Builders LLC", "DEMO-MC-0004");
        Issue(hood, 9, 6, "RCPT-10570");
        Inspect(hood, InspectionType.RoughMechanical, dana, 1);

        var service = New(PermitType.Electrical, pat, "77 Ipsum Ave, Demoville, ZZ 00000",
            "Relocate electrical service mast and meter base to side of house.", 3_400m, 24, "Sparky Sample Electric", "DEMO-EL-0005");
        Issue(service, 23, 20, "RCPT-10490");
        Inspect(service, InspectionType.RoughElectrical, dana, -14, InspectionResult.Passed);
        Inspect(service, InspectionType.FinalElectrical, dana, 0);

        db.SaveChanges();
    }

    /// <summary>
    /// <paramref name="offset"/> days from today, moved off weekends: past dates back to Friday,
    /// today and future dates forward to Monday (inspections only happen on weekdays).
    /// </summary>
    private static DateOnly Weekday(DateTime now, int offset)
    {
        var d = DateOnly.FromDateTime(now).AddDays(offset);
        var step = offset < 0 ? -1 : 1;
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(step);
        return d;
    }

    /// <summary>Writes a one-page placeholder PDF that the seeded documents point to.</summary>
    private static string WriteSamplePdf(string uploadDirectory)
    {
        const string name = "sample-plan.pdf";
        Directory.CreateDirectory(uploadDirectory);
        var path = Path.Combine(uploadDirectory, name);
        if (File.Exists(path))
            return name;

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            null, // content stream, filled below
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        const string text = "BT /F1 20 Tf 72 700 Td (PermitTrack sample document) Tj 0 -30 Td /F1 12 Tf (Placeholder for a site plan or scope of work.) Tj ET";
        objects[3] = $"<< /Length {text.Length} >>\nstream\n{text}\nendstream";

        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = sb.Length;
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
        return name;
    }
}
