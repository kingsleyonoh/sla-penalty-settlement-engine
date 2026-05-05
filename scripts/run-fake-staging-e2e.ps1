$ErrorActionPreference = "Stop"

dotnet test tests/Slapen.Application.Tests/Slapen.Application.Tests.fsproj --filter "FullyQualifiedName~fake staging ecosystem flow accrues settles posts and emits Hub event" --logger "console;verbosity=minimal"
