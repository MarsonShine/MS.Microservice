using System;
using System.Text.RegularExpressions;

namespace MS.Microservice.Core.Common.Advance.Resilience.RetryCondition
{
    /// <summary>
    /// 字符串匹配重试条件（适用于你的JSON解析场景）
    /// </summary>
    public class RegexMatchRetryCondition : IRetryCondition
    {
        private readonly Regex _regex;
        private readonly bool _shouldMatchToRetry;

        public RegexMatchRetryCondition(string pattern, bool shouldMatchToRetry = true)
        {
            _regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.Singleline);
            _shouldMatchToRetry = shouldMatchToRetry;
        }

        public bool ShouldRetry<T>(T result, Exception? exception)
        {
            if (result is string stringResult)
            {
                bool isMatch = _regex.IsMatch(stringResult);
                return _shouldMatchToRetry ? isMatch : !isMatch;
            }

            return false;
        }
    }
}
