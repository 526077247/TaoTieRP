using System;
using UnityEngine;

namespace TaoTie.RenderPipelines
{
    public enum ShowIfOperator
    {
        IsTrue,
        NotEqual,
        Equal,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public class VisibleIfAttribute : PropertyAttribute
    {
        public string FieldName { get; }
        public ShowIfOperator Operator { get; }
        public double Value { get; }

        public VisibleIfAttribute(string fieldName)
            : this(fieldName, ShowIfOperator.IsTrue, 0d) { }

        public VisibleIfAttribute(string fieldName, ShowIfOperator op, double value)
        {
            FieldName = fieldName;
            Operator = op;
            Value = value;
        }
    }
}
