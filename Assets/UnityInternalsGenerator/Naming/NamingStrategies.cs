namespace DandaUtils.Editor.UnityInternalsGenerator.Naming
{
    using System;

    public static class NamingStrategies
    {
        private static readonly INamingStrategy s_Exact = new ExactNaming();
        private static readonly INamingStrategy s_Pascal = new PascalCaseNaming();

        public static INamingStrategy For(NamingStyle style)
        {
            switch (style)
            {
                case NamingStyle.Exact: return s_Exact;
                case NamingStyle.PascalCase: return s_Pascal;
                default: throw new ArgumentOutOfRangeException(nameof(style), style, null);
            }
        }
    }
}
