using System.Collections.Generic;

namespace Generator
{
    public class Tag
    {
        public string Key { get; set; }
        public string Value { get; set; }

        public Tag()
        {

        }

        public Tag(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }

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

        /// <summary>
        /// List of custom tags to replace in files.
        /// </summary>
        public List<Tag> CustomTags { get; set; } = new List<Tag>() { new Tag("[Example]", "Example") };
    }
}