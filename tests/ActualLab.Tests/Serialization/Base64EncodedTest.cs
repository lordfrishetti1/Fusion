namespace ActualLab.Tests.Serialization;

public class Base64EncodedTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        var b0 = default(Base64Encoded);
        b0.Count.Should().Be(0);
        b0.Data.Length.Should().Be(0);
        b0.Encode().Should().Be("");

        var b0a = new Base64Encoded([]);
        b0a.Count.Should().Be(0);
        b0a.Data.Length.Should().Be(0);
        b0a.Encode().Should().Be("");
        Equals(b0, b0a).Should().BeTrue();

        var b1 = new Base64Encoded([1]);
        b1.Count.Should().Be(1);
        b1.Data.Length.Should().Be(1);
        Equals(b0, b1).Should().BeFalse();
    }
}
