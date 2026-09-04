namespace CropQc.Api.Tests;

public sealed class PhotoOrientationClientTests
{
    [Fact]
    public void Rotation_client_guards_double_submit_refreshes_revision_and_cache_busts_images()
    {
        var script = Read("src", "CropQc.Web", "wwwroot", "js", "photo-orientation.js");

        Assert.Contains("card.classList.contains(\"photo-card-saving\")", script);
        Assert.Contains("fetch(form.action", script);
        Assert.Contains("data-photo-presentation-revision", script);
        Assert.Contains("payload.presentationRevision", script);
        Assert.Contains("url.searchParams.set(\"v\"", script);
        Assert.Contains("payload.error", script);
        Assert.Contains("aria-live", Read("src", "CropQc.Web", "Views", "Shared", "_PhotoGroups.cshtml"));
    }

    [Fact]
    public void Rotation_controls_are_touch_friendly_and_stack_without_phone_overflow()
    {
        var css = Read("src", "CropQc.Web", "wwwroot", "css", "site.css");

        Assert.Contains(".photo-rotate-button", css);
        Assert.Contains("min-height: 42px", css);
        Assert.Contains("grid-template-columns: 1fr 1fr", css);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
