using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Attributes;

namespace MC2Neos
{
    class Options
    {
        [RequiredArgument(0, "dir", "Directory containing the Minecraft map(s)")]
        public string Directory { get; set; }

        [OptionalArgument(null, "login", "Login Credential")]
        public string Login { get; set; }

        [OptionalArgument(null, "pasword", "Login Password")]
        public string Password { get; set; }

        [OptionalArgument(null, "token", "2FA Token")]
        public string TwoFactorToken { get; set; }
    }
}
