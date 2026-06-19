using System;
using System.Collections.Generic;
using MCServerLauncher.Common.Extensibility;

namespace MCServerLauncher.WPF.Services;

public interface IPageRegistry
{
    void Register(string id, Type pageType, PageTarget target, int order = 0);
    IReadOnlyList<PageRegistration> GetPages(PageTarget target);
    Type? GetPageType(string id);
}

public record PageRegistration(string Id, Type PageType, PageTarget Target, int Order);
