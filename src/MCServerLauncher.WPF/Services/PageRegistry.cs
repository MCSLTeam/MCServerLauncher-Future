using System;
using System.Collections.Generic;
using System.Linq;
using MCServerLauncher.Common.Extensibility;

namespace MCServerLauncher.WPF.Services;

public class PageRegistry : IPageRegistry
{
    private readonly List<PageRegistration> _registrations = [];

    public void Register(string id, Type pageType, PageTarget target, int order = 0)
    {
        _registrations.RemoveAll(r => r.Id == id);
        _registrations.Add(new PageRegistration(id, pageType, target, order));
    }

    public IReadOnlyList<PageRegistration> GetPages(PageTarget target)
    {
        return _registrations
            .Where(r => r.Target == target)
            .OrderBy(r => r.Order)
            .ToList();
    }

    public Type? GetPageType(string id)
    {
        return _registrations.FirstOrDefault(r => r.Id == id)?.PageType;
    }
}
