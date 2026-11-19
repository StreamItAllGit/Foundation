## Summary

<!-- Briefly describe what this pull request changes and why. -->

## Type of Change

- [ ] Bug fix (non-breaking change addressing an issue)
- [ ] New feature or SDK primitive
- [ ] Documentation or release hygiene update
- [ ] Build, test, or CI improvement

## Verification Checklist

- [ ] `dotnet test Dev/Foundation.sln -c Release -p:TreatWarningsAsErrors=true` passes with zero warnings
- [ ] `python -m unittest discover -s Dev/tests/foundation_core -v` passes
- [ ] Changes keep tests isolated (`APP_ISOLATED_TEST=1`, temp dirs only) without mutating host registry or user directories
- [ ] Zero workstation paths, secrets, tokens, or proprietary assets included
- [ ] Updated `Dev/docs/` / `CHANGELOG.md` where public APIs or behavior changed
