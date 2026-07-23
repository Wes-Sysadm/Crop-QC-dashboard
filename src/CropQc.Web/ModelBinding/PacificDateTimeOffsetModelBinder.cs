using System.Globalization;
using CropQc.Shared.Time;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CropQc.Web.ModelBinding;

public sealed class PacificDateTimeOffsetModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueResult == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueResult);
        var value = valueResult.FirstValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            if (Nullable.GetUnderlyingType(bindingContext.ModelType) is not null)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }

            return Task.CompletedTask;
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                out var offsetValue)
            && HasExplicitOffset(value))
        {
            bindingContext.Result = ModelBindingResult.Success(offsetValue);
            return Task.CompletedTask;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var localValue))
        {
            try
            {
                var timezone = PacificBusinessTimeService.ResolvePacificTimeZone();
                var unspecified = DateTime.SpecifyKind(localValue, DateTimeKind.Unspecified);
                if (timezone.IsInvalidTime(unspecified))
                {
                    bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "That Pacific time does not exist because of the daylight-saving transition.");
                    return Task.CompletedTask;
                }

                var offset = timezone.IsAmbiguousTime(unspecified)
                    ? timezone.GetAmbiguousTimeOffsets(unspecified).Max()
                    : timezone.GetUtcOffset(unspecified);
                bindingContext.Result = ModelBindingResult.Success(new DateTimeOffset(unspecified, offset).ToUniversalTime());
                return Task.CompletedTask;
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "Pacific business time is unavailable.");
                return Task.CompletedTask;
            }
        }

        bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "Enter a valid Pacific date and time.");
        return Task.CompletedTask;
    }

    private static bool HasExplicitOffset(string value)
    {
        var trimmed = value.Trim();
        return trimmed.EndsWith('Z')
            || trimmed.LastIndexOf('+') > trimmed.IndexOf('T')
            || (trimmed.LastIndexOf('-') > trimmed.IndexOf('T'));
    }
}

public sealed class PacificDateTimeOffsetModelBinderProvider : IModelBinderProvider
{
    private static readonly IModelBinder Binder = new PacificDateTimeOffsetModelBinder();

    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var type = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;
        return type == typeof(DateTimeOffset) ? Binder : null;
    }
}
