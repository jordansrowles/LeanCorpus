using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;
using Xunit;

namespace Rowles.Text.Tests;

[Category(TestCategory.Unit)]
[Area(TestArea.Tokenisers)]
public sealed class PathTreeTokeniserTests
{
    static MaterialisingTokenSink M() => new();

    [Fact] public void UnixPath() { var s=M(); new PathTreeTokeniser{Lowercase=false}.Tokenise("/usr/local/bin",s); Assert.Equal(3,s.Tokens.Count); Assert.Equal("/usr",s.Tokens[0].Text); Assert.Equal("/usr/local",s.Tokens[1].Text); Assert.Equal("/usr/local/bin",s.Tokens[2].Text); }
    [Fact] public void WindowsPath() { var s=M(); new PathTreeTokeniser{Lowercase=false}.Tokenise(@"C:\Users\Docs",s); Assert.Equal(2,s.Tokens.Count); Assert.Equal(@"C:\Users",s.Tokens[0].Text); Assert.Equal(@"C:\Users\Docs",s.Tokens[1].Text); }
    [Fact] public void MixedSeparators() { var s=M(); new PathTreeTokeniser{Lowercase=false}.Tokenise("C:/Users\\Docs",s); Assert.Equal(2,s.Tokens.Count); Assert.Equal("C:/Users",s.Tokens[0].Text); Assert.Equal("C:/Users\\Docs",s.Tokens[1].Text); }
    [Fact] public void RelativePath() { var s=M(); new PathTreeTokeniser{Lowercase=false}.Tokenise("src/app/models",s); Assert.Equal(3,s.Tokens.Count); Assert.Equal("src",s.Tokens[0].Text); Assert.Equal("src/app",s.Tokens[1].Text); Assert.Equal("src/app/models",s.Tokens[2].Text); }
    [Fact] public void SingleSegment() { var s=M(); new PathTreeTokeniser{Lowercase=false}.Tokenise("config",s); Assert.Single(s.Tokens); Assert.Equal("config",s.Tokens[0].Text); }
    [Fact] public void EmptyInput() { var s=M(); new PathTreeTokeniser().Tokenise("",s); Assert.Empty(s.Tokens); }
    [Fact] public void UncPath() { var s=M(); new PathTreeTokeniser{Lowercase=false}.Tokenise(@"\\server\share\folder",s); Assert.True(s.Tokens.Count>=1); Assert.Contains(@"\\server\share\folder",s.Tokens.Select(t=>t.Text)); }
    [Fact] public void ConsecutiveSeparators() { var s=M(); new PathTreeTokeniser{Lowercase=false}.Tokenise("/usr//local",s); Assert.True(s.Tokens.Count>=1); Assert.Contains("/usr//local",s.Tokens.Select(t=>t.Text)); }
    [Fact] public void S3Uri() { var s=M(); new PathTreeTokeniser{Lowercase=false}.Tokenise("s3://my-bucket/logs/2026",s); Assert.Equal(2,s.Tokens.Count); Assert.Equal("s3://my-bucket/logs",s.Tokens[0].Text); Assert.Equal("s3://my-bucket/logs/2026",s.Tokens[1].Text); }
    [Fact] public void FileUri() { var s=M(); new PathTreeTokeniser{Lowercase=false}.Tokenise("file:///home/user/docs",s); Assert.Equal(3,s.Tokens.Count); Assert.Equal("file:///home",s.Tokens[0].Text); Assert.Equal("file:///home/user",s.Tokens[1].Text); Assert.Equal("file:///home/user/docs",s.Tokens[2].Text); }
    [Fact] public void Lowercase() { var s=M(); new PathTreeTokeniser{Lowercase=true}.Tokenise(@"C:\Users\MYDOCS",s); Assert.Equal(@"c:\users",s.Tokens[0].Text); Assert.Equal(@"c:\users\mydocs",s.Tokens[1].Text); }
    [Fact] public void LowercaseOff() { var s=M(); new PathTreeTokeniser{Lowercase=false}.Tokenise(@"C:\Users\MyDocs",s); Assert.Equal(@"C:\Users",s.Tokens[0].Text); Assert.Equal(@"C:\Users\MyDocs",s.Tokens[1].Text); }
    [Fact] public void DepthPayloads() { var s=M(); new PathTreeTokeniser{Lowercase=false,EmitDepthPayloads=true}.Tokenise("/a/b/c",s); Assert.Equal(3,s.Tokens.Count); Assert.Equal(0,BitConverter.ToInt32(s.Tokens[0].Payload!)); Assert.Equal(1,BitConverter.ToInt32(s.Tokens[1].Payload!)); Assert.Equal(2,BitConverter.ToInt32(s.Tokens[2].Payload!)); }
    [Fact] public void DepthPayloadsOff() { var s=M(); new PathTreeTokeniser{Lowercase=false}.Tokenise("/a/b/c",s); Assert.All(s.Tokens,t=>Assert.Null(t.Payload)); }
    [Fact] public void SuffixMode() { var s=M(); new PathTreeTokeniser{Lowercase=false,SuffixMode=true}.Tokenise("/src/app/models/user.cs",s); Assert.Equal(4,s.Tokens.Count); Assert.Equal("user.cs",s.Tokens[0].Text); Assert.Equal("models/user.cs",s.Tokens[1].Text); Assert.Equal("app/models/user.cs",s.Tokens[2].Text); Assert.Equal("/src/app/models/user.cs",s.Tokens[3].Text); }
    [Fact] public void SuffixModeWithDepth() { var s=M(); new PathTreeTokeniser{SuffixMode=true,EmitDepthPayloads=true}.Tokenise("/src/app/models/user.cs",s); Assert.Equal(4,s.Tokens.Count); Assert.Equal(0,BitConverter.ToInt32(s.Tokens[0].Payload!)); Assert.Equal(1,BitConverter.ToInt32(s.Tokens[1].Payload!)); Assert.Equal(2,BitConverter.ToInt32(s.Tokens[2].Payload!)); Assert.Equal(3,BitConverter.ToInt32(s.Tokens[3].Payload!)); }
    [Fact] public void TokenType() { var s=M(); new PathTreeTokeniser{Lowercase=false}.Tokenise("/a/b",s); Assert.All(s.Tokens,t=>Assert.Equal("path",t.Type)); }
    [Fact] public void Offsets() { var s=M(); new PathTreeTokeniser{Lowercase=false}.Tokenise("/usr/bin",s); Assert.Equal(0,s.Tokens[0].StartOffset); Assert.Equal(4,s.Tokens[0].EndOffset); Assert.Equal(0,s.Tokens[1].StartOffset); Assert.Equal(8,s.Tokens[1].EndOffset); }
    [Fact] public void TrailingSeparator() { var s=M(); new PathTreeTokeniser{Lowercase=false}.Tokenise("/usr/local/",s); Assert.Equal(2,s.Tokens.Count); Assert.Equal("/usr",s.Tokens[0].Text); Assert.Equal("/usr/local",s.Tokens[1].Text); }
}
