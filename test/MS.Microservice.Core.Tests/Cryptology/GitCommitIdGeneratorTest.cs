using MS.Microservice.Core.Common;
using MS.Microservice.Core.Security.Cryptology;
using System;

namespace MS.Microservice.Core.Tests.Cryptology
{
	public class GitCommitIdGeneratorTest
	{
		[Fact]
		public void TestEncrypt()
		{
			string commitId = GitCommitIdGenerator.GenerateCommitId(
			"这里是文件内容",
			"这是一个提交信息",
			"作者名字",
			DateTime.Now);
			Assert.NotEmpty(commitId);
		}

		[Fact]
		public void TestEncrypt2()
		{
			string commitId = GitCommitIdGenerator.GenerateCommitId2(
			"这里是文件内容",
			"这是一个提交信息",
			"作者名字",
			DateTime.Now);
			Assert.NotEmpty(commitId);
		}
	}
}
