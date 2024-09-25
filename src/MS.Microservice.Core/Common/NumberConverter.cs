using System;

namespace MS.Microservice.Core.Common
{
	public class NumberConverter
	{
		public static string DecimalToBinary(int decimalNumber)
		{
			return Convert.ToString(decimalNumber, 2);
		}

		public static int BinaryToDecimal(string binaryNumber)
		{
			return Convert.ToInt32(binaryNumber, 2);
		}

		// 移位操作: 相当于number * 2^shift
		public static int LeftShift(int number, int shift)
		{
			return number << shift;
		}

		/// <summary>
		/// 逻辑右移：相当于number / 2^shift
		/// 不保留符号位：无论是正数还是负数，左边都补0
		/// </summary>
		/// <param name="number"></param>
		/// <param name="shift"></param>
		/// <returns></returns>
		public static int RightLogicShift(int number, int shift)
		{
			return number >>> shift;
			//return (int)((uint)number >> shift);
		}
		/// <summary>
		/// 算术右移：相当于number / 2^shift
		/// 保留符号位：对于正数，左边补0；对于负数，左边补1
		/// </summary>
		/// <param name="number"></param>
		/// <param name="shift"></param>
		/// <returns></returns>
		public static int RightShift(int number, int shift)
		{
			return number >> shift;
		}
	}
}
