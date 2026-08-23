using System.Reflection;

namespace AalgTrips
{
    /// <summary>
    /// Exposes the git commit the running app was built from, so the site footer
    /// can show which revision is live. The commit is baked into the assembly's
    /// informational version at build time via the <c>SourceRevisionId</c> MSBuild
    /// property — set from <c>GITHUB_SHA</c> when CI publishes, and from
    /// <c>git rev-parse</c> for a local build — which the .NET SDK appends after a
    /// <c>+</c>. When no revision was stamped (a build with neither an override nor
    /// a git repo) the sha is empty and the footer shows a neutral label instead.
    /// </summary>
    public static class BuildInfo
    {
        private const string RepositoryUrl = "https://github.com/gep13-oss/aalg-trips";

        /// <summary>
        /// Gets the full commit sha the app was built from, or an empty string when
        /// no revision was stamped into this build.
        /// </summary>
        public static string CommitSha { get; } = ResolveCommitSha();

        /// <summary>
        /// Gets the first seven characters of <see cref="CommitSha"/> (the customary
        /// short form), or an empty string when no revision was stamped.
        /// </summary>
        public static string ShortSha =>
            CommitSha.Length >= 7 ? CommitSha.Substring(0, 7) : CommitSha;

        /// <summary>
        /// Gets the GitHub URL of the commit the app was built from, or an empty
        /// string when no revision was stamped.
        /// </summary>
        public static string CommitUrl =>
            CommitSha.Length == 0 ? string.Empty : RepositoryUrl + "/commit/" + CommitSha;

        private static string ResolveCommitSha()
        {
            string informational = typeof(BuildInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (string.IsNullOrEmpty(informational))
            {
                return string.Empty;
            }

            // The SDK appends the source revision after the version as "1.0.0+sha".
            int plus = informational.LastIndexOf('+');
            return plus >= 0 && plus < informational.Length - 1
                ? informational.Substring(plus + 1)
                : string.Empty;
        }
    }
}