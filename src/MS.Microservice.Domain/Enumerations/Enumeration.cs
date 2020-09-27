using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace MS.Microservice.Domain.Enumerations
{
    /// <summary>
    /// copy from https://lostechies.com/jimmybogard/2008/08/12/enumeration-classes/
    /// </summary>
    abstract public class Enumeration : IComparable
    {
        private readonly int _value;
        private readonly string _name;

        protected Enumeration(int value, string name)
        {
            _value = value;
            _name = name;
        }

        public int Value => _value;

        public string Name => _name;

        public override string ToString() => Name;

        public static IEnumerable<T> GetAll<T>() where T : Enumeration, new()
        {
            var type = typeof(T);
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (var info in fields)
            {
                var instance = new T();

                if (info.GetValue(instance) is T locatedValue)
                {
                    yield return locatedValue;
                }
            }
        }

        public override bool Equals([AllowNull]object obj)
        {
            if (!(obj is Enumeration otherValue))
                return false;

            var typeMatches = GetType().Equals(obj.GetType());
            var valueMatches = _value.Equals(otherValue.Value);

            return typeMatches && valueMatches;
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }

        public static int AbsoluteDifference(Enumeration firstValue, Enumeration secondValue)
        {
            var absoluteDifference = Math.Abs(firstValue.Value - secondValue.Value);
            return absoluteDifference;
        }

        public static T FromValue<T>(int value) where T : Enumeration, new()
        {
            var matchingItem = Parse<T, int>(value, "value", item => item.Value == value);
            return matchingItem;
        }

        private static T Parse<T, K>(K value, string name, Func<T, bool> predicate) where T : Enumeration, new()
        {
            var matchingItem = GetAll<T>().FirstOrDefault(predicate);

            if(matchingItem == null!)
            {
                var message = string.Format("'{0}' is not a valid {1} in {2}", value, name, typeof(T));
                throw new ApplicationException(message);
            }

            return matchingItem;
        }

        public int CompareTo(object? obj) => Value.CompareTo(((Enumeration)obj!).Value);

        public static bool operator ==(Enumeration left, Enumeration right)
        {
            if (ReferenceEquals(left, null))
            {
                return ReferenceEquals(right, null);
            }

            return left.Equals(right);
        }

        public static bool operator !=(Enumeration left, Enumeration right)
        {
            return !(left == right);
        }

        public static bool operator <(Enumeration left, Enumeration right)
        {
            return ReferenceEquals(left, null) ? !ReferenceEquals(right, null) : left.CompareTo(right) < 0;
        }

        public static bool operator <=(Enumeration left, Enumeration right)
        {
            return ReferenceEquals(left, null) || left.CompareTo(right) <= 0;
        }

        public static bool operator >(Enumeration left, Enumeration right)
        {
            return !ReferenceEquals(left, null) && left.CompareTo(right) > 0;
        }

        public static bool operator >=(Enumeration left, Enumeration right)
        {
            return ReferenceEquals(left, null) ? ReferenceEquals(right, null) : left.CompareTo(right) >= 0;
        }
    }

    /// <summary>
    /// https://github.com/HeadspringLabs/Enumeration/blob/master/Enumeration.cs
    /// </summary>
    /// <typeparam name="TEnumeration"></typeparam>
    [Serializable]
    [DebuggerDisplay("{DisplayName} - {Value}")]
    public abstract class Enumeration<TEnumeration> : Enumeration<TEnumeration, int>
        where TEnumeration : Enumeration<TEnumeration>
    {
        protected Enumeration(int value, string displayName)
            : base(value, displayName)
        {
        }

        public static TEnumeration FromInt32(int value)
        {
            return FromValue(value);
        }

        public static bool TryFromInt32(int listItemValue, out TEnumeration result)
        {
            return TryParse(listItemValue, out result);
        }
    }

    public abstract class Enumeration<TEnumeration, TValue> : IComparable<TEnumeration>, IEquatable<TEnumeration>
        where TEnumeration : Enumeration<TEnumeration, TValue>
        where TValue : IComparable
    {
        private static readonly Lazy<TEnumeration[]> Enumerations = new Lazy<TEnumeration[]>(GetEnumerations);

        [DataMember(Order = 1)]
        readonly string _displayName;

        [DataMember(Order = 0)]
        readonly TValue _value;

        protected Enumeration(TValue value, string displayName)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            _value = value;
            _displayName = displayName;
        }

        public TValue Value
        {
            get { return _value; }
        }

        public string DisplayName
        {
            get { return _displayName; }
        }

        public int CompareTo([AllowNull]TEnumeration other)
        {
            return Value.CompareTo(other! == default(TEnumeration)! ? default : other.Value);
        }

        public override sealed string ToString()
        {
            return DisplayName;
        }

        public static TEnumeration[] GetAll()
        {
            return Enumerations.Value;
        }

        private static TEnumeration[] GetEnumerations()
        {
            Type enumerationType = typeof(TEnumeration);
            return enumerationType
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(info => enumerationType.IsAssignableFrom(info.FieldType))
                .Select(info => info.GetValue(null))
                .Cast<TEnumeration>()
                .ToArray();
        }

        public override bool Equals([AllowNull]object obj)
        {
            return Equals(obj as TEnumeration);
        }

        public bool Equals([AllowNull]TEnumeration other)
        {
            return other! != null! && ValueEquals(other.Value);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public static bool operator ==(Enumeration<TEnumeration, TValue>? left, Enumeration<TEnumeration, TValue>? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Enumeration<TEnumeration, TValue>? left, Enumeration<TEnumeration, TValue>? right)
        {
            return !Equals(left, right);
        }

        public static TEnumeration FromValue(TValue value)
        {
            return Parse(value, "value", item => item.Value.Equals(value));
        }

        public static TEnumeration Parse(string displayName)
        {
            return Parse(displayName, "display name", item => item.DisplayName == displayName);
        }

        static bool TryParse(Func<TEnumeration, bool> predicate, out TEnumeration result)
        {
            result = GetAll().FirstOrDefault(predicate);
            return result != null;
        }

        private static TEnumeration Parse(object value, string description, Func<TEnumeration, bool> predicate)
        {

            if (!TryParse(predicate, out TEnumeration result))
            {
                string message = string.Format("'{0}' is not a valid {1} in {2}", value, description, typeof(TEnumeration));
                throw new ArgumentException(message, "value");
            }

            return result;
        }

        public static bool TryParse(TValue value, out TEnumeration result)
        {
            return TryParse(e => e.ValueEquals(value), out result);
        }

        public static bool TryParse(string displayName, out TEnumeration result)
        {
            return TryParse(e => e.DisplayName == displayName, out result);
        }

        protected virtual bool ValueEquals(TValue value)
        {
            return Value.Equals(value);
        }
    }
}
