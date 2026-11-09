using System.Text.Json.Serialization;
namespace TidyYard.Models;

[JsonConverter(typeof(JsonStringEnumConverter<FenceSort>))]
public enum FenceSort { Name, DateModified, Type, Size }
