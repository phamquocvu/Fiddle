﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml;
using Fiddle.Compilers;
using Fiddle.Compilers.Implementation;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.Win32;

namespace Fiddle.UI {
    public static class Helper {
        public static string LanguageToFriendly(Language language) {
            return language.GetDescription();
        }

        public static ICompiler NewCompiler(Language language, string sourceCode, Editor caller,
            string langVersion = null) {
            IExecutionProperties exProps = new ExecutionProperties(App.Preferences.ExecuteTimeout);
            ICompilerProperties comProps = new CompilerProperties(App.Preferences.ExecuteTimeout, langVersion);
            var properties = new Compilers.Properties(language, sourceCode,
                App.Preferences.NetImports,
                App.Preferences.JdkPath,
                App.Preferences.PyPath,
                exProps, comProps,
                new FiddleGlobals(caller));
            return Host.NewCompiler(properties);
        }

        public static IHighlightingDefinition LoadXshd(string resourceName) {
            var type = typeof(Helper);
            string fullName = $"{type.Namespace}.Syntax.{resourceName}";
            using (var stream = type.Assembly.GetManifestResourceStream(fullName)) {
                if (stream == null)
                    return null;
                using (var reader = new XmlTextReader(stream)) {
                    return HighlightingLoader.Load(reader, HighlightingManager.Instance);
                }
            }
        }

        public static IHighlightingDefinition LoadXshd(Language language) {
            return LoadXshd($"{language}.xshd");
        }

        public static async Task<ICompiler> LoadDragDrop(DragEventArgs args, Editor caller, ICompiler currentCompiler) {
            var compiler = currentCompiler;

            if (args.Data.GetDataPresent(DataFormats.FileDrop))
                if (args.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) {
                    string file = files[0];
                    string content;
                    var fileInfo = new FileInfo(file);
                    using (var stream = fileInfo.OpenRead()) {
                        byte[] buffer = new byte[fileInfo.Length];
                        await stream.ReadAsync(buffer, 0, (int) fileInfo.Length);
                        content = Encoding.Default.GetString(buffer);
                    }

                    var lang = GetLanguageForFilename(file) ?? currentCompiler.Language;
                    if (currentCompiler.Language != lang) {
                        compiler = NewCompiler(lang, content, caller);
                        string name = LanguageToFriendly(lang);
                        foreach (ComboBoxItem value in caller.ComboBoxLanguage.Items)
                            if (value.Content.ToString() == name) {
                                caller.ComboBoxLanguage.SelectedValue = value;
                                App.Preferences.SelectedLanguage = caller.SelectedLanguage;
                            }
                        caller.Title = $"Fiddle - {name}";
                    }
                    caller.TextBoxCode.Text = content;
                }
            return compiler;
        }


        public static string SaveFile(string content) {
            var dialog = new SaveFileDialog {
                Filter = "Text Files (*.txt)|*.txt|All files (*.*)|*.*",
                FilterIndex = 1,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            bool? result = dialog.ShowDialog();
            if (result == true)
                File.WriteAllText(dialog.FileName, content);
            return dialog.FileName;
        }

        public static string SaveFile(string code, Language language) {
            var dialog = new SaveFileDialog {
                Filter = GetFilterForLanguage(language),
                FilterIndex = 1,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            bool? result = dialog.ShowDialog();
            if (result == true)
                File.WriteAllText(dialog.FileName, code);
            return dialog.FileName;
        }

        private static Language? GetLanguageForFilename(string filename) {
            if (filename.EndsWith(".cpp"))
                return Language.Cpp;
            if (filename.EndsWith(".cs"))
                return Language.CSharp;
            if (filename.EndsWith(".py"))
                return Language.Python;
            if (filename.EndsWith(".vb"))
                return Language.Vb;
            if (filename.EndsWith(".java"))
                return Language.Java;
            if (filename.EndsWith(".lua"))
                return Language.Lua;

            return null;
        }

        private static string GetFilterForLanguage(Language language) {
            switch (language) {
                case Language.Cpp:
                    return "C++ source files (*.cpp)|*.cpp|All files (*.*)|*.*";
                case Language.CSharp:
                    return "C# source files (*.cs)|*.cs|All files (*.*)|*.*";
                case Language.Python:
                    return "Python source files (*.py)|*.py|All files (*.*)|*.*";
                case Language.Vb:
                    return "Visual Basic source files (*.vb)|*.vb|All files (*.*)|*.*";
                case Language.Java:
                    return "Java source files (*.java)|*.java|All files (*.*)|*.*";
                case Language.Lua:
                    return "LUA source files (*.lua)|*.lua|All files (*.*)|*.*";
                default:
                    return "All files (*.*)|*.*";
            }
        }


        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(ref Win32Point pt);

        public static Point GetMousePosition() {
            var w32Mouse = new Win32Point();
            GetCursorPos(ref w32Mouse);
            return new Point(w32Mouse.X, w32Mouse.Y);
        }

        private static Uri BuildHelpLink(string errorMessage) {
            string query = WebUtility.UrlEncode(errorMessage);
            return new Uri($"https://stackoverflow.com/search?q={query}"); //premium help service
        }

        public static string ConcatErrors(IEnumerable<Exception> errorsList) {
            string errors = "";
            const int maxErrors = 7; //do not show more than [maxErrors] errors in Message
            int countErrors = 0;

            IEnumerable<Exception> exceptions = errorsList as Exception[] ?? errorsList.ToArray(); //kill multiple enums
            foreach (var ex in exceptions) {
                errors += $"#{++countErrors}: {ex.Message}{Environment.NewLine}";

                if (countErrors <= maxErrors) continue;
                //not shown errors (limited to [maxErrors])
                int notShown = exceptions.Count() - countErrors;
                if (notShown > 0)
                    errors += $"{Environment.NewLine}(and {notShown} more..)";
                break;
            }

            return errors;
        }

        public static IEnumerable<Inline> BuildDiagnostics(IEnumerable<IDiagnostic> diagnostics, string indent = "") {
            IList<Inline> items = new List<Inline>();
            int counter = 1;
            string nl = Environment.NewLine;
            foreach (var diagnostic in diagnostics) {
                Brush brush;
                switch (diagnostic.Severity) {
                    case Severity.Error:
                        brush = Brushes.Red;
                        break;
                    case Severity.Warning:
                        brush = Brushes.Yellow;
                        break;
                    default:
                        brush = Brushes.LightGray;
                        break;
                }
                string lines = diagnostic.LineFrom != diagnostic.LineTo
                    ? $"{diagnostic.LineFrom}-{diagnostic.LineTo}"
                    : diagnostic.LineFrom.ToString();

                items.Add(new Run($"{indent}#{counter++} Ln{lines}: ") {Foreground = Brushes.LightGray});
                if (diagnostic.Severity == Severity.Error) {
                    var url = BuildHelpLink(diagnostic.Message);
                    var childInline = new Run(diagnostic.Message + nl) {Foreground = brush};
                    var link = new Hyperlink(childInline) {Foreground = brush, NavigateUri = url};
                    link.Click += delegate { Process.Start(url.ToString()); };
                    items.Add(link);
                } else {
                    items.Add(new Run(diagnostic.Message + nl) {Foreground = brush});
                }
            }
            return items;
        }

        public static IEnumerable<Inline> BuildRuns(IExecuteResult result) {
            string nl = Environment.NewLine;
            if (result.Success) {
                //Execute: SUCCESS, Compile: SUCCESS
                List<Inline> items = new List<Inline> {
                    new Run($"Execution successful! (Took {result.Time}ms){nl}") {
                        Foreground = Brushes.Green,
                        FontWeight = FontWeights.Bold,
                        FontSize = 15
                    }
                };

                if (result.ReturnValue == null) {
                    //NO RETURN VALUE
                    items.Add(new Run($"Return value: /{nl}") {Foreground = Brushes.Gray});
                } else {
                    //RETURN VALUE(S)
                    var type = result.ReturnValue.GetType();
                    items.Add(new Run("Return value: "));
                    string typeName;
                    if (type.IsGenericType)
                        typeName = $"{type.Name.Remove(type.Name.IndexOf('`'))}" +
                                   $"<{string.Join(", ", type.GenericTypeArguments.Select(a => a.Name))}>";
                    else typeName = type.Name;
                    items.Add(new Run($"({typeName}) ") {Foreground = Brushes.CadetBlue});
                    if (type.IsArray) {
                        //MULTIPLE RETURN VALUES
                        var array = (Array) result.ReturnValue;
                        string run = string.Join(", ", array.Cast<object>());
                        items.Add(new Run($"{run}{nl}") {
                            Foreground = Brushes.Orange,
                            FontFamily = new FontFamily("Consolas")
                        });
                    } else if (result.ReturnValue is IEnumerable && type.IsGenericType) {
                        //MULTIPLE RETURN VALUES
                        var enumerable = (IEnumerable) result.ReturnValue;
                        string run = string.Join(", ", enumerable.Cast<object>());
                        items.Add(new Run($"{run}{nl}") {
                            Foreground = Brushes.Orange,
                            FontFamily = new FontFamily("Consolas")
                        });
                    } else {
                        //SINGLE RETURN VALUE
                        items.Add(new Run($"{result.ReturnValue}{nl}") {
                            Foreground = Brushes.Orange,
                            FontFamily = new FontFamily("Consolas")
                        });
                    }
                }
                if (string.IsNullOrWhiteSpace(result.ConsoleOutput)) {
                    //NO CONSOLE OUTPUT
                    items.Add(new Run($"Console output: /{nl}") {Foreground = Brushes.Gray});
                } else {
                    //CONSOLE OUTPUT
                    items.Add(new Run("Console output: "));
                    items.Add(new Run(result.ConsoleOutput) {
                        Foreground = Brushes.Orange,
                        FontFamily = new FontFamily("Consolas")
                    });
                }
                if (result.CompileResult.Diagnostics?.Any() == true) {
                    //DIAGNOSTICS
                    items.Add(new Run("Diagnostics:\n"));
                    items.AddRange(BuildDiagnostics(result.CompileResult.Diagnostics, " "));
                }
                return items;
            }

            if (result.CompileResult.Success) {
                //Execute: FAIL, Compile: SUCCESS
                List<Inline> items = new List<Inline> {
                    new Run($"Execution failed! (Took {result.Time}ms){nl}") {
                        Foreground = Brushes.Red,
                        FontWeight = FontWeights.Bold,
                        FontSize = 15
                    }
                };

                if (result.Exception == null) {
                    //NO ERROR MESSAGE
                    items.Add(new Run($"An unexpected error occured.{nl}") {Foreground = Brushes.Gray});
                } else {
                    //ERROR MESSAGE
                    items.Add(new Run($"Ln{result.ExceptionLineNr}: {result.Exception.GetType().Name}: ") {
                        Foreground = Brushes.OrangeRed
                    });
                    var url = BuildHelpLink(result.Exception.Message);
                    var childInline = new Run($"\"{result.Exception.Message}\"{nl}") {Foreground = Brushes.OrangeRed};
                    var link = new Hyperlink(childInline) {Foreground = Brushes.OrangeRed, NavigateUri = url};
                    link.Click += delegate { Process.Start(url.ToString()); };
                    items.Add(link);
                }
                if (result.CompileResult.Diagnostics?.Any() == true) {
                    //DIAGNOSTICS
                    items.Add(new Run("Diagnostics:\n"));
                    items.AddRange(BuildDiagnostics(result.CompileResult.Diagnostics, " "));
                }

                return items;
            }

            //Execute: FAIL, Compile: FAIL
            return BuildRuns(result.CompileResult);
        }

        public static IEnumerable<Inline> BuildRuns(ICompileResult result) {
            string nl = Environment.NewLine;
            if (result.Success) {
                //Compile: SUCCESS
                List<Inline> items = new List<Inline> {
                    new Run($"Compilation successful! (Took {result.Time}ms){nl}") {
                        Foreground = Brushes.Green,
                        FontWeight = FontWeights.Bold,
                        FontSize = 15
                    }
                };
                if (result.Diagnostics?.Any() == true) {
                    items.Add(new Run("Diagnostics:\n"));
                    items.AddRange(BuildDiagnostics(result.Diagnostics, " "));
                }
                return items;
            } else {
                //Compile: FAIL
                List<Inline> items = new List<Inline> {
                    new Run($"Compilation failed!{nl}") {
                        Foreground = Brushes.Red,
                        FontWeight = FontWeights.Bold,
                        FontSize = 15
                    }
                };
                if (result.Diagnostics?.Any() == true) {
                    //DIAGNOSTICS
                    items.Add(new Run("Diagnostics:\n"));
                    items.AddRange(BuildDiagnostics(result.Diagnostics, " "));
                }
                return items;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Win32Point {
            public int X;
            public int Y;
        }
    }
}