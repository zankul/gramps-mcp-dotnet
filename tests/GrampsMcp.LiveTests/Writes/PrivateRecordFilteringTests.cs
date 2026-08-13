using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GrampsMcp.LiveTests.LiveStack;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;

namespace GrampsMcp.LiveTests.Writes;

/// <summary>
/// Pins docs/PLAN.md fact #15 and the corrected "Private records" guardrail: a JWT lacking
/// <c>ViewPrivate</c> does not get 403'd on a private record — it gets a silently filtered
/// response instead (an empty-looking <c>200</c> on a single-object <c>GET</c>, a <c>404</c> via a
/// collection filter). This is the upstream behavior that motivates <see cref="GrampsUpdateTool"/>
/// now requiring <c>ViewPrivate</c>: its read-modify-write would otherwise merge a patch onto the
/// redacted object and write the redaction back permanently. Creates its own throwaway GUEST-role
/// user via <c>POST /api/users/{user_name}/</c> (role <c>0</c> is the only built-in role whose
/// permission set excludes <c>ViewPrivate</c> — verified directly against the running container's
/// <c>gramps_webapi.auth.const.PERMISSIONS</c> mapping) rather than relying on any manually
/// provisioned account, so this test is self-contained against a fresh
/// <c>docker-compose.test.yml</c> stack. Uses the reserved <see cref="ZzTestNamespace"/> and
/// <see cref="TestObjectScope"/> cleanup, same as <see cref="WriteRoundTripTests"/> — never touches
/// the seeded <c>example.gramps</c> data.
/// </summary>
[Trait("Tier", "Live")]
[Collection(LiveGrampsCollection.Name)]
public class PrivateRecordFilteringTests(LiveGrampsFixture fixture)
{
    [Fact]
    public async Task GuestLackingViewPrivate_GetsSilentlyFilteredRecord_NotA403()
    {
        LiveStackGate.Require(fixture);
        LoopbackGuard.RequireLocal(fixture.Options.ApiUrl);

        var createTool = new GrampsCreateTool(fixture.ApiClient);
        var updateTool = new GrampsUpdateTool(fixture.ApiClient);

        await using var scope = new TestObjectScope(fixture.ApiClient);

        var grampsId = ZzTestNamespace.NextGrampsId();
        var payload = JsonSerializer.SerializeToElement(new
        {
            gramps_id = grampsId,
            primary_name = new
            {
                first_name = "PrivateRecordTest",
                surname_list = new[] { new { surname = "ZzTestSurname" } },
            },
            gender = 2,
        });

        var createResult = await createTool.CreateAsync("person", payload, CancellationToken.None);
        var handle = createResult[0].GetProperty("handle").GetString()!;
        scope.Track("person", handle);

        await updateTool.UpdateAsync(
            "person", handle, JsonSerializer.SerializeToElement(new { @private = true }), CancellationToken.None);

        var username = $"zztest-guest-{Guid.NewGuid():N}"[..24];
        const string guestPassword = "zztest-guest-pw-1!";

        using var createUserResponse = await fixture.RawClient.PostAsJsonAsync(
            $"/api/users/{username}/",
            new { email = $"{username}@example.invalid", full_name = "ZzTest Guest", password = guestPassword, role = 0 },
            CancellationToken.None);
        createUserResponse.EnsureSuccessStatusCode();

        try
        {
            using var tokenClient = new HttpClient { BaseAddress = new Uri(fixture.Options.ApiUrl) };
            var guestToken = await RateLimitRetry.RunAsync<string?>(async () =>
            {
                using var tokenResponse = await tokenClient.PostAsJsonAsync(
                    "/api/token/", new { username, password = guestPassword }, CancellationToken.None);

                if (tokenResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    return new RateLimitedAttempt<string?>(RateLimitOutcome.RateLimited, null);
                }

                tokenResponse.EnsureSuccessStatusCode();
                var body = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
                return new RateLimitedAttempt<string?>(RateLimitOutcome.Succeeded, body.GetProperty("access_token").GetString());
            }, CancellationToken.None);

            Assert.NotNull(guestToken);

            using var guestClient = new HttpClient { BaseAddress = new Uri(fixture.Options.ApiUrl) };
            guestClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", guestToken);

            using var singleObjectResponse = await guestClient.GetAsync($"/api/people/{handle}", CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, singleObjectResponse.StatusCode);

            var body = await singleObjectResponse.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
            Assert.False(
                body.TryGetProperty("primary_name", out _),
                "Expected a GET on a private record, from an account lacking ViewPrivate, to come back " +
                "silently redacted (no primary_name) rather than 403 or the full record — this is exactly " +
                "the case GrampsUpdateTool's ViewPrivate requirement guards against (docs/PLAN.md fact #15).");

            using var collectionResponse = await guestClient.GetAsync(
                $"/api/people/?gramps_id={grampsId}&page=1&pagesize=1", CancellationToken.None);
            Assert.Equal(
                HttpStatusCode.NotFound,
                collectionResponse.StatusCode);
        }
        finally
        {
            // Guest accounts aren't Gramps objects, so TestObjectScope can't track them — delete
            // directly as the owner so repeated local runs against the same stack don't accumulate
            // zztest-guest-* users indefinitely.
            using var deleteUserResponse = await fixture.RawClient.DeleteAsync($"/api/users/{username}/", CancellationToken.None);
            deleteUserResponse.EnsureSuccessStatusCode();
        }
    }
}
