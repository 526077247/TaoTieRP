using System;

namespace TaoTie.RenderPipelines
{
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public class LabelTextAttribute : Attribute
    {
        public string Text { get; }

        public LabelTextAttribute(string text)
        {
            Text = text;
        }
    }
}
