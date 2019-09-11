namespace Generator
{
    public class Configuration
    {
        /// <summary>
        /// This is the folder markdown files are read from.
        /// </summary>
        public string SourcePath { get; set; } = "Source\\";

        /// <summary>
        /// This is the folder where the generator templates are.
        /// </summary>
        public string TemplateDirectory { get; set; } = "Templates\\";

        /// <summary>
        /// This is the folder where the built site will be placed.
        /// </summary>
        public string OutputDirectory { get; set; } = "Output\\";

        /// <summary>
        /// Prefix to all links.
        /// </summary>
        public string WebPrefix { get; set; } = "";

        /// <summary>
        /// The name of the folder in which the built source files will reside inside the output directory.
        /// </summary>
        public string SourceOutput { get; set; } = "Files\\";

        /// <summary>
        /// The name of the wiki.
        /// </summary>
        public string WikiTitle { get; set; } = "Wiki";
    }
}