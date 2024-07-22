using System.Globalization;
using System.Text.RegularExpressions;

namespace MCServerLauncher.Daemon.Remote.Action
{
    internal static class ActionTypeExtensions
    {
        public static string ToShakeCase(this ActionType actionType)
        {
            return Regex.Replace(actionType.ToString(), "([a-z])([A-Z])", "$1_$2").ToLower();
        }

        public static ActionType FromSnakeCase(string snakeCase)
        {
            var bigCamelCase = string.Concat(
                snakeCase.Split("_").Select(part => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(part.ToLower()))
            );
            return (ActionType)Enum.Parse(typeof(ActionType), bigCamelCase);
        }
    }

    internal enum ActionType
    {
        Message,
        Ping,
        FileUploadRequest,
        FileUploadChunk,
    }
}