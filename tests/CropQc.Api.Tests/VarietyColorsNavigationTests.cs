using System.Security.Claims;
using CropQc.Web.Controllers;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;

namespace CropQc.Api.Tests;

public sealed class VarietyColorsNavigationTests
{
    [Theory]
    [InlineData(PageAccessLevel.View, false)]
    [InlineData(PageAccessLevel.Admin, true)]
    public async Task DedicatedPage_UsesExistingAccessLevelWithoutChangingConfiguration(
        PageAccessLevel level,
        bool expectedCanManage)
    {
        var colors = new TrackingVarietyColorService();
        var controller = Controller(colors, new FixedAccess(level));

        var result = Assert.IsType<ViewResult>(await controller.VarietyColors(CancellationToken.None));
        var model = Assert.IsType<VarietyColorsAdminViewModel>(result.Model);

        Assert.Equal(expectedCanManage, model.CanManage);
        Assert.Equal(1, colors.PageLoads);
        Assert.Equal(0, colors.Writes);
    }

    [Fact]
    public async Task SuccessfulSaveAndReset_ReturnToDedicatedPage()
    {
        var colors = new TrackingVarietyColorService();
        var controller = Controller(colors, new FixedAccess(PageAccessLevel.Admin));
        var form = new VarietyColorForm { VarietyKey = "GALA", VarietyName = "Gala", HexColor = "#112233" };

        var save = Assert.IsType<RedirectToActionResult>(await controller.SaveVarietyColor(form, CancellationToken.None));
        var reset = Assert.IsType<RedirectToActionResult>(await controller.ResetVarietyColor(form, CancellationToken.None));

        Assert.Equal(nameof(AdminController.VarietyColors), save.ActionName);
        Assert.Equal(nameof(AdminController.VarietyColors), reset.ActionName);
        Assert.Equal(2, colors.Writes);
    }

    private static AdminController Controller(IVarietyColorService colors, IUserAccessService access)
    {
        var controller = new AdminController(
            null!,
            new AdminAuthorizationService(),
            null!,
            null!,
            colors,
            access,
            new ConfigurationBuilder().Build())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Email, "colors@example.com")],
                        "Test"))
                }
            }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, new TestTempDataProvider());
        return controller;
    }

    private sealed class FixedAccess(PageAccessLevel level) : IUserAccessService
    {
        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) =>
            Task.FromResult(areaKey == ApplicationAreas.VarietyColors && level >= minimumLevel);

        public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) =>
            Task.FromResult(areaKey == ApplicationAreas.VarietyColors ? level : PageAccessLevel.None);

        public void InvalidateAll()
        {
        }
    }

    private sealed class TrackingVarietyColorService : IVarietyColorService
    {
        public int PageLoads { get; private set; }
        public int Writes { get; private set; }

        public Task<VarietyColorsAdminViewModel> GetAdminPageAsync(bool canManage, CancellationToken cancellationToken)
        {
            PageLoads++;
            return Task.FromResult(new VarietyColorsAdminViewModel { CanManage = canManage });
        }

        public Task<string?> SaveAsync(VarietyColorForm form, string changedByEmail, CancellationToken cancellationToken)
        {
            Writes++;
            return Task.FromResult<string?>(null);
        }

        public Task<string?> ResetAsync(VarietyColorForm form, string changedByEmail, CancellationToken cancellationToken)
        {
            Writes++;
            return Task.FromResult<string?>(null);
        }

        public Task<IReadOnlyDictionary<string, VarietyColorResolved>> GetResolvedColorsAsync(IEnumerable<string> varietyKeys, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, VarietyColorResolved>> GetResolvedColorsReadOnlyAsync(IEnumerable<string> varietyKeys, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, VarietyColorResolved>> GetResolvedColorsForMasterDataAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
