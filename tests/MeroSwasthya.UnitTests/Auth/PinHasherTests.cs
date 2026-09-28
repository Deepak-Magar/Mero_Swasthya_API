using MeroSwasthya.Modules.Auth.Application;

namespace MeroSwasthya.UnitTests.Auth;

public sealed class PinHasherTests
{
    private readonly PinHasher _hasher = new();

    [Fact]
    public void Hash_is_an_argon2id_phc_string_with_owasp_parameters()
    {
        _hasher.Hash("1234").Should().MatchRegex(@"^\$argon2id\$v=19\$m=19456,t=2,p=1\$[A-Za-z0-9+/]{22}\$[A-Za-z0-9+/]{43}$");
    }

    [Fact]
    public void Verify_accepts_the_right_pin_and_rejects_others()
    {
        var hash = _hasher.Hash("1234");
        _hasher.Verify("1234", hash).Should().BeTrue();
        _hasher.Verify("1235", hash).Should().BeFalse();
        _hasher.Verify("1234", null).Should().BeFalse();
        _hasher.Verify("1234", "$bcrypt$whatever").Should().BeFalse();
    }

    [Fact]
    public void Same_pin_hashes_differently_each_time()
    {
        _hasher.Hash("1234").Should().NotBe(_hasher.Hash("1234"));
    }
}
