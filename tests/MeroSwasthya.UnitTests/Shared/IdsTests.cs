using MeroSwasthya.Shared.Ids;

namespace MeroSwasthya.UnitTests.Shared;

public sealed class IdsTests
{
    private static readonly Guid Ns = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    [Fact]
    public void Uuid5_matches_the_rfc4122_reference_vector()
    {
        // Python: uuid.uuid5(uuid.NAMESPACE_DNS, "python.org")
        Uuid5.Create(Ns, "python.org").ToString().Should().Be("886313e1-3b8a-5372-9b90-0c9aee199e5d");
    }

    [Theory] // reference values computed with Python's uuid.uuid5 — the same algorithm the app's `uuid` package uses
    [InlineData(1, "e497e31f-7f0b-512f-9c46-a83714f8faf7")]
    [InlineData(4, "35447ff5-3a5c-5941-b65e-8b29165ed579")]
    [InlineData(8, "974e9ecf-60cb-58ed-a71f-09a89cff0d69")]
    public void AncContactId_is_uuid5_of_pregnancyId_colon_contactNo(int contactNo, string expected)
    {
        Ids.AncContactId("pg_b2b2b2b2-0000-4000-8000-000000000001", contactNo).Should().Be(expected);
    }

    [Fact]
    public void ImmunisationId_uses_unpadded_dose_number()
    {
        Ids.ImmunisationId("p_a1a1a1a1-0000-4000-8000-000000000006", "MR", 2)
            .Should().Be("69d94120-890e-5117-a7bc-30c1cffda021");
    }

    [Fact]
    public void Uuid5_encodes_names_as_utf8()
    {
        Uuid5.Create(Ns, "पाठ:1").ToString().Should().Be("2113ffe4-a011-5123-aaff-fe4a8d696fc1");
    }

    [Theory]
    [InlineData("p_a1a1a1a1-0000-4000-8000-000000000001", true)]
    [InlineData("0d9c0a1e-2a4b-4c8d-9e1f-3a5b7c9d1e2f", true)]
    [InlineData("rx_0001", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("_leading", false)]
    [InlineData("x/../y", false)]
    public void Client_ids_accept_uuid_and_prefixed_forms(string id, bool valid)
    {
        Ids.IsValidClientId(id).Should().Be(valid);
    }

    [Fact]
    public void New_ids_carry_the_prefix()
    {
        Ids.New("u").Should().MatchRegex("^u_[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[0-9a-f]{4}-[0-9a-f]{12}$");
    }
}
