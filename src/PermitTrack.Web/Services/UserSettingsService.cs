using Microsoft.EntityFrameworkCore;
using PermitTrack.Web.Data;
using PermitTrack.Web.Domain;

namespace PermitTrack.Web.Services;

/// <summary>Loads and saves the signed-in user's preferences. Scoped, so the layout and page share one lookup per request.</summary>
public class UserSettingsService(AppDbContext db)
{
    private readonly Dictionary<int, UserSettings> _cache = [];

    /// <summary>The user's saved settings, or defaults if they've never saved any.</summary>
    public async Task<UserSettings> GetAsync(int userId)
    {
        if (_cache.TryGetValue(userId, out var cached))
            return cached;
        var settings = await db.Settings.FirstOrDefaultAsync(s => s.UserId == userId) ?? new UserSettings { UserId = userId };
        return _cache[userId] = settings;
    }

    public async Task SaveAsync(UserSettings settings)
    {
        if (db.Entry(settings).State == EntityState.Detached)
            db.Settings.Add(settings);
        await db.SaveChangesAsync();
        _cache[settings.UserId] = settings;
    }

    public async Task ResetAsync(int userId)
    {
        await db.Settings.Where(s => s.UserId == userId).ExecuteDeleteAsync();
        _cache.Remove(userId);
    }

    public async Task SetWidgetVisibilityAsync(int userId, UserRole role, string widgetId, bool visible)
    {
        var settings = await GetAsync(userId);
        settings.SetWidgets(role, settings.GetWidgets(role).Select(w => w.Id == widgetId ? w with { Visible = visible } : w));
        await SaveAsync(settings);
    }
}
