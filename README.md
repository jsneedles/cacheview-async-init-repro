# CacheView async initialization reproduction

Minimal .NET 11 Blazor static SSR reproduction of `CacheView` hanging subsequent requests after asynchronous parent initialization.

## Requirements

- .NET SDK 11.0.100 or a later stable .NET 11 SDK.
- No additional NuGet packages, credentials, databases, or external services.

Originally reproduced with SDK 11.0.100 and Microsoft.AspNetCore.App 11.0.0 on macOS ARM64.

## Run

Open `CacheViewRepro.slnx` in an IDE, or run from this directory:

```sh
dotnet run --project CacheViewRepro.csproj --no-launch-profile
```

The app starts Kestrel on an automatically assigned loopback port, sends HTTP requests to itself, prints the results, and shuts down. Each request has a two-second client timeout. It does not connect to external services.

## Observed results

Example output from the reproduction (timings vary):

```text
plain request 1: 197ms, cached markers unchanged: True
plain request 2: TIMED OUT after 2005ms
plain request 3: TIMED OUT after 2007ms
guarded request 1: 58ms, cached markers unchanged: True
guarded request 2: 52ms, cached markers unchanged: True
guarded request 3: 56ms, cached markers unchanged: True
Concurrent cold requests: 0 of 30 timed out
```

For the first successful request in each variant, `cached markers unchanged: True` is the baseline; subsequent successful requests compare the response with the previous response.

The process reports observations rather than asserting success or failure via its exit code.

## Reproduction and control

`Home.razor` contains two sibling `CacheView` components beneath a page with `[StreamRendering(false)]`. The page's `OnInitializedAsync` waits 50 milliseconds to force an initial render followed by a rerender.

- `/?variant=plain` creates the cache views during the initial render. The first request completes, but later requests time out.
- `/?variant=guarded` creates the cache views only after asynchronous initialization finishes. Repeated requests complete and replay identical markup, including the generated GUIDs.
- `/?variant=concurrent` uses the same guard with a separate, initially cold cache partition. The runner sends 30 requests concurrently and reports how many time out.

`VaryBy` isolates the three variants. The reproduction uses the default in-memory CacheView store, without HybridCache or interactive components. The failure was also reproduced with only one CacheView before adding the second sibling.

## Expected behavior

An asynchronous parent initialization and its resulting rerender should not abandon a CacheView cache fill. The first request should populate the entry, and subsequent requests should complete using the cached content without requiring an initialization guard.

## Suspected cause

`CacheView.SetParametersAsync` replaces its `RenderState` on a parameter update. On a same-instance rerender before HTML emission, `CacheViewService.PrepareAsync` creates a new state and takes the duplicate-resolution path. This appears to discard the original state's capture completion and pending store task. Later requests then wait for a cache fill that is never completed.

This is a source-based explanation of the observed behavior, not a framework-maintainer-confirmed diagnosis.

Related implementation: https://github.com/dotnet/aspnetcore/pull/65772

The reproduction does not involve the duplicate interactive render modes described in https://github.com/dotnet/aspnetcore/issues/70330.
