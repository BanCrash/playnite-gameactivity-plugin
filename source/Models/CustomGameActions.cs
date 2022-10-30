using System;
using System.Collections.Generic;

namespace GameActivity.Models
{
    public class CustomGameActions
    {
        public string Name { get; set; }
        public List<string> DefaultFor { get; set; }

        public CustomGameActions(string name)
        {
            Name = name;
            DefaultFor = new List<string>();
        }
    }
}
