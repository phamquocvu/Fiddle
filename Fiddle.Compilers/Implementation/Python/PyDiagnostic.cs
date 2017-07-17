﻿using Microsoft.Scripting;
using System;

namespace Fiddle.Compilers.Implementation.Python {
    public class PyDiagnostic : IDiagnostic {
        public string Message { get; }
        public int LineFrom { get; }
        public int LineTo { get; }
        public int CharFrom { get; }
        public int CharTo { get; }
        public Severity Severity { get; }

        public PyDiagnostic(string message, int lnFrom, int lnTo, int chFrom, int chTo, Severity severity) {
            Message = message;
            LineFrom = lnFrom;
            LineTo = lnTo;
            CharFrom = chFrom;
            CharTo = chTo;
            Severity = severity;
        }

        public Exception ToException() {
            return new Exception($"[{Severity}] Ln{LineFrom}-{LineTo} Ch{CharFrom}-{CharTo}: {Message}");
        }
    }
}
