using MS.Microservice.Core.Security.Cryptology;
using MS.Microservice.Core.Security.Summary;
using System;
using System.Text;

/*
 * Git 提交内容的生成的唯一ID是基于哈希算法生成的。
 * 实现方式通过用户提交的内容以及提交附带的元数据，比如提交信息、作者、提交日期等。
 * 通过这种方式，Git 能够确保每个提交都有一个独特的标识符，即使在不同的仓库之间也能保持唯一性。
 */
namespace MS.Microservice.Core.Common
{
	public class GitCommitIdGenerator
	{
		public static string GenerateCommitId(string content, string commitMessage, string author, DateTime commitDate)
		{
			// 将输入数据组合成一个字符串
			string inputData = $"{content}{commitMessage}{author}{commitDate:o}";
			return CryptologyHelper.SHA256(inputData);
		}

		public static string GenerateCommitId2(string content, string commitMessage, string author, DateTime commitDate)
		{
			// 将输入数据组合成一个字符串
			string inputData = $"{content}{commitMessage}{author}{commitDate:o}";
			return CryptologyHelper.SHA256(Md5.Encrypt(inputData, Encoding.UTF8));
		}
	}
}
