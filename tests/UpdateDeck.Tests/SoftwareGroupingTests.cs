using UpdateDeck.Core;
using Xunit;

namespace UpdateDeck.Tests;

public class SoftwareGroupingTests
{
    [Theory]
    [InlineData("Microsoft .NET Runtime - 8.0.12 (x64)","Microsoft Corporation",".NET")]
    [InlineData("Microsoft ASP.NET Core 9.0.0 Shared Framework","Microsoft Corporation",".NET")]
    [InlineData("Microsoft Windows Desktop Runtime - 6.0.36 (x86)","Microsoft Corporation",".NET")]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x86)","Microsoft Corporation","Microsoft Visual C++")]
    [InlineData("Visual Studio Code","Microsoft Corporation","Visual Studio Code")]
    [InlineData("Adobe Photoshop 2025","Adobe Inc.","Adobe")]
    [InlineData("Adobe Acrobat","Adobe Inc.","Adobe")]
    [InlineData("Example Tool 1.2.3","","Example Tool")]
    [InlineData("Unrelated Product","","Unrelated Product")]
    [InlineData("paint.net","dotPDN LLC","dotPDN")]
    public void RelatedEntriesShareDisplayFamilyWithoutChangingIdentity(string name,string publisher,string expected)
    {
        var app=new Software{Id="original-instance",Name=name,Publisher=publisher,InstalledVersion="1.2.3"};
        Assert.Equal(expected,SoftwareGrouping.Family(app));
        Assert.Equal("original-instance",app.Id);Assert.Equal("1.2.3",app.InstalledVersion);
    }
}
