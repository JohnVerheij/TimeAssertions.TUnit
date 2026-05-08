// Mirrors the GlobalUsings.cs recommendation documented in TimeAssertions.TUnit's README.
// The smoke-test project deliberately uses <ImplicitUsings>disable</ImplicitUsings> so a
// failure to wire up these usings: or a future change that breaks the auto-discovery of
// TimeAssertions.TUnit's [AssertionExtension]-emitted entry points: surfaces as a build
// failure here rather than silently passing in our own test project (which lives in the
// TimeAssertions.TUnit.Tests namespace and gets parent-namespace visibility for free).

global using System;                                // TimeSpan, DateTimeOffset, Action
global using System.Threading;                      // CancellationToken
global using System.Threading.Tasks;                // Task
global using Microsoft.Extensions.Time.Testing;     // FakeTimeProvider
global using TimeAssertions;                        // TimeRenderingHelpers (rendering helpers)
global using TimeAssertions.TUnit;                  // WithinTimeBudgetAssertion / WithinTimeBudgetCapturingAssertion
