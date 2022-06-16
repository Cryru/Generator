#region Using

using System.Collections.Generic;

#endregion

namespace Generator
{
    public class Node
    {
        public string Name = "";
        public string File = "";
        public List<Node> Children = new List<Node>();
    }
}