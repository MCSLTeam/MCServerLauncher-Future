# Basic Daemon Plugin Template

This template is a minimal starting point for a trusted startup daemon plugin.

## Use

1. Copy this directory outside `docs/templates`.
2. Rename `Example.Plugin.csproj`, namespace, package id, and assembly name.
3. Run:

```bash
dotnet publish -c Release -p:MCSLPluginBundle=true -o artifacts/plugins/community.example.basic
```

The published directory must contain the entry assembly and `mcsl-plugin.json`.
