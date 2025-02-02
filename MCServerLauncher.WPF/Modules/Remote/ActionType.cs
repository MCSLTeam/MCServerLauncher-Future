﻿using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MCServerLauncher.WPF.Modules.Remote;

public static class ActionTypeExtensions
{
    public static string ToShakeCase(this ActionType actionType)
    {
        return Regex.Replace(actionType.ToString(), "([a-z])([A-Z])", "$1_$2").ToLower();
    }

    public static ActionType FromSnakeCase(string snakeCase)
    {
        var bigCamelCase = string.Concat(
            snakeCase.Split('_').Select(part => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(part.ToLower()))
        );
        return (ActionType)Enum.Parse(typeof(ActionType), bigCamelCase);
    }
}

public enum ActionType
{
    Ping,
    GetJavaList,
    GetSystemInfo,
    FileUploadRequest,
    FileUploadChunk,
    FileUploadCancel
}