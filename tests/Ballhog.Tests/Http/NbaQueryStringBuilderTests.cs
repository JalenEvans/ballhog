using Ballhog.Http;
using Xunit;

namespace Ballhog.Tests.Http;

/// <summary>
/// Behavior tests for <see cref="NbaQueryStringBuilder"/> (requirement R4, chunk C-004).
/// The builder must emit <c>LeagueID</c> first, order the remaining parameters
/// deterministically by ordinal key, and return the query component with no leading '?'.
/// </summary>
public sealed class NbaQueryStringBuilderTests
{
    [Fact]
    public void Build_emits_league_id_first_with_no_leading_question_mark()
    {
        string query = NbaQueryStringBuilder.Build("00", new KeyValuePair<string, string?>("Season", "2025-26"));

        Assert.Equal("LeagueID=00&Season=2025-26", query);
    }

    [Fact]
    public void Build_with_no_extra_parameters_emits_only_league_id()
    {
        Assert.Equal("LeagueID=00", NbaQueryStringBuilder.Build("00"));
    }

    [Fact]
    public void Build_orders_remaining_parameters_by_ordinal_key()
    {
        string query = NbaQueryStringBuilder.Build(
            "00",
            new KeyValuePair<string, string?>("Season", "2025-26"),
            new KeyValuePair<string, string?>("PlayerID", "2544"),
            new KeyValuePair<string, string?>("DateFrom", "2025-10-01"));

        Assert.Equal("LeagueID=00&DateFrom=2025-10-01&PlayerID=2544&Season=2025-26", query);
    }

    [Fact]
    public void IEnumerable_overload_matches_the_params_overload_output()
    {
        IEnumerable<KeyValuePair<string, string?>> parameters =
            new List<KeyValuePair<string, string?>> { new("Season", "2025-26") };

        Assert.Equal("LeagueID=00&Season=2025-26", NbaQueryStringBuilder.Build("00", parameters));
    }

    [Fact]
    public void Build_omits_parameters_with_null_values()
    {
        // The XML contract does not specify null handling. We fix that contract here:
        // a null value is omitted entirely (rather than emitted as an empty value).
        string query = NbaQueryStringBuilder.Build(
            "00",
            new KeyValuePair<string, string?>("Season", "2025-26"),
            new KeyValuePair<string, string?>("PlayerID", null));

        Assert.Equal("LeagueID=00&Season=2025-26", query);
    }
}
