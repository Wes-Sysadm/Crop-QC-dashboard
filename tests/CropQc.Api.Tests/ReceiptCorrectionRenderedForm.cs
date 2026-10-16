using System.Net;
using System.Text.RegularExpressions;

namespace CropQc.Api.Tests;

// Extract this view's successful controls, not database values. RoomId is initialized
// by Edit.cshtml's populateRooms from data-selected-room-id; correction source stays disabled.
internal static class ReceiptCorrectionRenderedForm
{
    internal static string Attribute(string tag, string name) => WebUtility.HtmlDecode(
        Regex.Match(tag, $"(?:^|\\s){Regex.Escape(name)}=\"([^\"]*)\"").Groups[1].Value);

    internal static Dictionary<string, string> Read(string html)
    {
        var form = Regex.Match(html, "<form class=\"form-grid\"[\\s\\S]*?</form>").Value;
        Assert.NotEmpty(form);
        var values = new Dictionary<string, string>();
        foreach (Match match in Regex.Matches(form, "<input\\b[^>]*>|<select\\b[^>]*>[\\s\\S]*?</select>"))
        {
            var tag = match.Value;
            var name = Attribute(tag, "name");
            if (name.Length == 0 || Regex.IsMatch(tag.Split('>')[0], "\\sdisabled(?:\\s|=|$)")) continue;
            var type = Attribute(tag, "type");
            if (type == "checkbox" && !Regex.IsMatch(tag, "\\schecked(?:\\s|=|/|>)")) continue;
            var value = Attribute(tag, "value");
            if (tag.StartsWith("<select"))
            {
                var options = Regex.Matches(tag, "<option\\b[^>]*>[\\s\\S]*?</option>").Select(x => x.Value).ToArray();
                var option = options.FirstOrDefault(x => Regex.IsMatch(x, "\\sselected(?:\\s|=|>)")) ?? options.FirstOrDefault();
                value = option == null ? Attribute(tag, "data-selected-room-id")
                    : Regex.IsMatch(option, "\\svalue=") ? Attribute(option, "value")
                    : WebUtility.HtmlDecode(Regex.Match(option, ">([^<]*)</option>").Groups[1].Value);
            }
            values.TryAdd(name, value); // MVC uses first value (checked checkbox before false hidden input).
        }
        Assert.False(values.ContainsKey("LotCode"));
        if (values.GetValueOrDefault("CorrectionSourceRoomId") == "") values.Remove("CorrectionSourceRoomId");
        foreach (Match field in Regex.Matches(form, "<textarea\\b[^>]*>[\\s\\S]*?</textarea>"))
            values[Attribute(field.Value, "name")] = WebUtility.HtmlDecode(Regex.Match(field.Value, ">([\\s\\S]*?)</textarea>").Groups[1].Value);
        return values;
    }

    internal static void Review(Dictionary<string, string> form, int quantity)
    {
        form["BinCount"] = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        form["Reason"] = "Verified receiving count correction";
        form["ConfirmInventoryChange"] = "true";
    }
}
