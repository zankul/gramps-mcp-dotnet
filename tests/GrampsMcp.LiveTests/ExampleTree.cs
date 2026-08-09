namespace GrampsMcp.LiveTests;

/// <summary>
/// Anchors and fingerprints captured from a real, running <c>docker-compose.test.yml</c> stack
/// seeded with the bundled <c>example.gramps</c> dataset. Every value here was read back from the
/// live API during capture — none were transcribed from memory or from upstream Gramps project
/// docs. See <c>docs/testing.md</c>'s "Capturing ExampleTree" section for the exact procedure to
/// re-capture these after an upstream image/dataset refresh.
///
/// Image: ghcr.io/gramps-project/grampsweb@sha256:6f81fa942f1d447a953e24eeddf17b28db9f5d55e5ba10ce2a76cfcf8896bb59
/// Captured: 2026-08-09
/// </summary>
internal static class ExampleTree
{
    // Anchor person: a well-connected individual (3 families, 3 events) picked by querying every
    // person's family_list/event_ref_list counts and taking the top result with a non-empty
    // surname — see docs/testing.md.
    public const string AnchorPersonGrampsId = "I0027";
    public const string AnchorPersonHandle = "B3UJQCZHDXII99AWW4";
    public const string AnchorPersonGivenName = "Matthias, Jr.";
    public const string AnchorPersonSurname = "Ball";

    // The anchor person's first (oldest) family — his marriage to Abigail Chapman Moreno.
    public const string AnchorFamilyGrampsId = "F0007";
    public const string AnchorFamilyHandle = "74UJQCKV8R4NBNHCB";

    // The anchor person's birthplace.
    public const string AnchorPlaceGrampsId = "P1349";
    public const string AnchorPlaceHandle = "L3UJQCWIJVOYAF2HRV";
    public const string AnchorPlaceName = "Amarillo";

    // The anchor person's birth event.
    public const string AnchorEventGrampsId = "E1322";
    public const string AnchorEventHandle = "a5af0ec5dd650c05e38";

    // A known relationship: the anchor person (I0027) and his son Jasper Ball (I0033), via
    // GET /api/relations/{handleA}/{handleB} (handles, not Gramps IDs).
    public const string RelatedChildGrampsId = "I0033";
    public const string RelatedChildHandle = "PCUJQCLY6DORHLPXOS";

    /// <summary>Exact <c>relationship_string</c> returned by
    /// <c>GET /api/relations/{AnchorPersonHandle}/{RelatedChildHandle}</c>.</summary>
    public const string AnchorToRelatedChildRelationship = "son";

    /// <summary>A surname with many hits, for pagination/search tests.</summary>
    public const string CommonSearchSurname = "Ball";

    /// <summary><c>X-Total-Count</c> returned by <c>GET /api/search/?query=Ball&amp;pagesize=1</c>
    /// as of capture.</summary>
    public const int CommonSearchSurnameHitCount = 68;

    /// <summary>
    /// <c>object_counts</c> from <c>GET /api/metadata/</c> as of capture. Sum: 10536.
    /// </summary>
    public static class ObjectCounts
    {
        public const int People = 2157;
        public const int Families = 762;
        public const int Events = 3432;
        public const int Places = 1296;
        public const int Citations = 2854;
        public const int Sources = 4;
        public const int Repositories = 3;
        public const int Media = 7;
        public const int Notes = 19;
        public const int Tags = 2;
    }
}
