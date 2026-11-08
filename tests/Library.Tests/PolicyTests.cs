using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Library.Core;
using Xunit;
namespace Library.Tests;
public sealed class PolicyTests
{
    [Theory]
    [InlineData("book.pdf",true)]
    [InlineData("books/a.epub",true)]
    [InlineData("../a.pdf",false)]
    [InlineData("C:\\a.pdf",false)]
    [InlineData("COM9.pdf",false)]
    [InlineData("a.pdf.",false)]
    [InlineData("a//b.pdf",false)]
    [InlineData("LPT3.pdf",false)]
    public void PathRules(string name,bool expected) => FilePolicy.IsSafeName(name).Should().Be(expected);
    [Fact]
    public void ThirteenEntriesRejected()
    {
        byte[] zip=Samples.Zip(Enumerable.Range(0,13).Select(i=>($"{i}.pdf",Samples.Pdf())).ToArray());
        Action act=()=>FilePolicy.Validate("bad.zip",zip);act.Should().Throw<RejectedFile>();
    }
    [Fact]
    public void TwelveEntriesAccepted()
    {
        byte[] zip=Samples.Zip(Enumerable.Range(0,12).Select(i=>($"{i}.pdf",Samples.Pdf())).ToArray());
        FilePolicy.Validate("good.zip",zip).Should().HaveCount(12);
    }
    [Fact]
    public void DamagedZipRejected()
    {
        Action act=()=>FilePolicy.Validate("bad.zip","PKbroken"u8.ToArray());act.Should().Throw<RejectedFile>();
    }
    [Fact]
    public void StreamLimitCountsActualBytes()
    {
        Action act=()=>FilePolicy.ReadLimited(new MemoryStream(new byte[11]),10);
        act.Should().Throw<RejectedFile>().Which.Status.Should().Be(413);
    }
    [Fact]
    public void EpubWithoutMimetypeRejected()
    {
        Action act=()=>FilePolicy.Validate("bad.epub",Samples.Zip(("a.xhtml","text"u8.ToArray())));
        act.Should().Throw<RejectedFile>();
    }
    [Fact]
    public void EpubDtdRejected()
    {
        byte[] zip=Samples.Zip(("mimetype","application/epub+zip"u8.ToArray()),
            ("META-INF/container.xml",Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///never'>]><container>&a;</container>")));
        Action act=()=>FilePolicy.Validate("bad.epub",zip);act.Should().Throw<RejectedFile>();
    }
    [Fact]
    public void SymbolicLinkRejected()
    {
        using var data=new MemoryStream();
        using(var zip=new ZipArchive(data,ZipArchiveMode.Create,true))
        {var e=zip.CreateEntry("link.pdf");e.ExternalAttributes=unchecked((int)0xA1FF0000);using var s=e.Open();s.Write(Samples.Pdf());}
        Action act=()=>FilePolicy.Validate("link.zip",data.ToArray());act.Should().Throw<RejectedFile>();
    }
    [Fact]
    public void CompressionBombRejected()
    {
        using var data=new MemoryStream();
        using(var zip=new ZipArchive(data,ZipArchiveMode.Create,true))
        {using var s=zip.CreateEntry("big.pdf",CompressionLevel.SmallestSize).Open();s.Write(new byte[500000]);}
        Action act=()=>FilePolicy.Validate("bomb.zip",data.ToArray());act.Should().Throw<RejectedFile>();
    }
}
