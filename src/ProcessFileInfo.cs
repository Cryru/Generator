﻿namespace Generator
{
    public class ProcessFileInfo
    {
        public string FileName;
        public string[] Contents;

        public ProcessFileInfo(string fileName, string[] contents)
        {
            FileName = fileName;
            Contents = contents;
        }
    }
}