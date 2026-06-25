# Security Policy

This is a personal ASP.NET Core / .NET learning scratchpad — no production
runtime, no real authentication backend, no user data, no exposed network
surface. Each `src/Step*` and `src/Part*` project is a placeholder
`WebApplication` whose `Program.cs` ends with a single hello-world endpoint
and is meant to be filled in by hand while following the matching guide
under `C:/MyFile/ArcForges/ArchitectureDesign/ASP.NetStudy/`.

If you spot a real security issue (malicious dependency update slipping
through Dependabot, a CI workflow weakness, a leaked secret in source),
please file a private report rather than a public issue so it can be
triaged before disclosure.
