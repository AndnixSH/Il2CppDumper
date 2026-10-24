namespace Il2CppDumper
{
    /// <summary>
    /// Maps an il2cpp global-metadata version to the range of Unity engine
    /// versions that produce it. Used for the auto-detected Unity version shown
    /// in the UI and logs, and to help pick matching il2cpp API headers.
    /// </summary>
    public static class UnityVersionMap
    {
        /// <summary>
        /// Returns a human-readable Unity version range for a metadata version
        /// (the fractional sub-version Il2CppDumper resolves, e.g. 24.4, 29.1).
        /// </summary>
        public static string GetUnityVersionRange(double metadataVersion)
        {
            // Order matters: check the most specific sub-versions first.
            return metadataVersion switch
            {
                >= 111 => "6000.7.0a6+",
                >= 108 => "6000.6.0f1+",
                >= 107 => "6000.5.5f1+",
                >= 106 => "6000.5.0f1 - 6000.5.4f1",
                >= 105 => "6000.5.0a5 - 6000.5.0b*",
                >= 104 => "6000.5.0a1 - 6000.5.0a4",
                >= 39 => "6000.3.0b1+",
                >= 38 => "6000.3.0a5 - 6000.3.0a6",
                >= 35 => "6000.3.0a2 - 6000.3.0a4",
                // 33 has no public Unity header; it is a short-lived 6000.3 alpha step
                >= 33 => "6000.3 alpha",
                >= 31 => "2022.3.33 - 2023.x / 6000.0 - 6000.2",
                >= 29.1 => "2021.3.0 - 2022.3.32",
                >= 29 => "2021.2.0 - 2021.2.x",
                >= 27.2 => "2021.1.0 - 2021.1.x",
                >= 27.1 => "2020.2.4 - 2020.3.x",
                >= 27 => "2020.2.0 - 2020.2.3",
                >= 24.5 => "2020.1.11 - 2020.1.x",
                >= 24.4 => "2020.1.0 - 2020.1.10",
                >= 24.3 => "2019.4.21 - 2019.4.x",
                >= 24.2 => "2019.3.7 - 2019.4.20",
                >= 24.15 => "2019.4.15 - 2019.4.20",
                >= 24.1 => "2018.4.x - 2019.3.6",
                >= 24 => "2017.1 - 2018.3",
                >= 23 => "5.6.x",
                >= 22 => "5.5.x",
                >= 21 => "5.3.5 - 5.4.x",
                >= 20 => "5.3.3 - 5.3.4",
                >= 19 => "5.3.2",
                >= 16 => "5.3.0 - 5.3.1",
                _ => "unknown",
            };
        }

        /// <summary>
        /// True for the Unity 6000.3 variable-width-index metadata formats.
        /// </summary>
        public static bool IsUnity6Plus(double metadataVersion) => metadataVersion >= 35;
    }
}
