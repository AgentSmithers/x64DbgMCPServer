namespace RGiesecke.DllExport
{
    using System;
    using System.Runtime.InteropServices;

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class DllExportAttribute : Attribute
    {
        public DllExportAttribute() { }
        public DllExportAttribute(string entryPoint) { EntryPoint = entryPoint; }
        public DllExportAttribute(string entryPoint, CallingConvention callingConvention)
        {
            EntryPoint = entryPoint;
            CallingConvention = callingConvention;
        }

        public string EntryPoint { get; }
        public CallingConvention CallingConvention { get; }
    }
}
