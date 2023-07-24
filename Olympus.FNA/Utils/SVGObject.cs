using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Olympus.Utils {
    public class SVGObject {
        public readonly List<SVGGroup> Groups = new();

        public readonly int Width;
        public readonly int Height;
        public readonly float TimeScale;

        public SVGObject(string source) {
            string svgMainTag = "";
            string currTag;
            string accTag = "";
            int tagStart = -1;
            int p = 0;
            while (p < source.Length) {
                if (source[p] == '<') {
                    tagStart = p;
                } else if (source[p] == '>') {
                    currTag = source[tagStart..(p+1)];
                    if (currTag.StartsWith("<g ")) {
                        accTag += currTag;
                    } else if (currTag.StartsWith("</g>")) {
                        accTag += currTag;
                        Groups.Add(new SVGGroup(accTag));
                        accTag = "";
                    } else if (currTag.StartsWith("<svg ")) {
                        svgMainTag = currTag;
                    } else if (accTag != "") {
                        accTag += currTag;
                    }

                    tagStart = -1;
                }

                p++;
            }
            
            string ParseData(string match, int p, string prefix) {
                int s = p + prefix.Length;
                p = s;
                while (match[p] != '\"') {
                    p++;
                }

                return match[s..p];
            }

            p = 0;
            while (p < svgMainTag.Length) { // two while loops, i dont care
                string range = svgMainTag[p..];
                if (range.StartsWith("width=")) {
                    Width = int.Parse(ParseData(svgMainTag, p, "width=\""));
                } else if (range.StartsWith("height=")) {
                    Height = int.Parse(ParseData(svgMainTag, p, "height=\""));
                } else if (range.StartsWith("timescale=")) {
                    TimeScale = float.Parse(ParseData(svgMainTag, p, "timescale=\""));
                }

                p++;
            }
            
        }

    }

    public class SVGGroup {

        public static readonly Regex XmlParser = new("<(?:\"[^\"]*\"[\'\"]*|\'[^\']*\'[\'\"]*|[^\'\">])+\\>");
        public readonly List<SVGPath> Paths = new List<SVGPath>();
        public readonly Color Stroke;
        public readonly int StrokeWidth;
        public readonly SVGStrokeLineCap StrokeLineCap;
        public readonly Color Fill;

        public SVGGroup(string gElement) {
            if (!gElement.StartsWith("<g "))
                throw new InvalidDataException($"Malformed group: {gElement}");

            string ParseData(string match, int p, string prefix) {
                int s = p + prefix.Length;
                p = s;
                while (match[p] != '\"') {
                    p++;
                }

                return match[s..p];
            }

            foreach (Match match in XmlParser.Matches(gElement)) {
                if (match.Value.StartsWith("<path")) {
                    Paths.Add(new SVGPath(match.Value));
                } else if (match.Value.StartsWith("<g")) {
                    int p = 0;
                    while (p < match.Value.Length) {
                        string range = match.Value[p..];
                        if (range.StartsWith("stroke=")) {
                            Stroke = ParseColor(ParseData(match.Value, p, "stroke=\""));
                        } else if (range.StartsWith("stroke-width=")) {
                            StrokeWidth = int.Parse(ParseData(match.Value, p, "stroke-width=\""));
                        } else if (range.StartsWith("stroke-linecap=")) {
                            string name = ParseData(match.Value, p, "stroke-linecap=\"");

                            foreach (SVGStrokeLineCap capType in Enum.GetValues<SVGStrokeLineCap>()) {
                                if (name.Equals(capType.ToString(), StringComparison.OrdinalIgnoreCase)) {
                                    StrokeLineCap = capType;
                                    break;
                                }
                            }
                        } else if (range.StartsWith("fill=")) {
                            string col = ParseData(match.Value, p, "fill=\"");
                            if (col.Equals("none"))
                                Fill = new Color(00, 00, 00, 00);
                            else
                                Fill = ParseColor(col);
                        }
                        p++;
                        
                    }

                } // ignore </g>
                
            }
        }

        public static Color ParseColor(string col) {
            if (!col.StartsWith('#')) throw new InvalidDataException($"Can't parse color: {col}");
            if (col.Length == 3 + 1) { // format #rgb -> #rgba
                col += "f";
            }

            if (col.Length == 4 + 1) { // format #rgba -> #rrggbbaa
                string temp = "#";
                for (int i = 1; i < 5; i++) {
                    temp += col[i];
                    temp += col[i];
                }

                col = temp;
            } else if (col.Length == 6 + 1) { // format #rrggbb -> #rrggbbaa
                col += "ff";
            }

            int r = Convert.ToInt32(col[1..3], 16);
            int g = Convert.ToInt32(col[3..5], 16);
            int b = Convert.ToInt32(col[5..7], 16);
            int a = Convert.ToInt32(col[7..9], 16);

            return new Color(r, g, b, a);


        }
    }

    public class SVGPath {
        public readonly List<SVGCommand> Commands = new();
        public static readonly Regex DElementParser = new("d\\=\"[A-Za-z0-9\\s\\,\\.\\-]+\"", RegexOptions.Compiled);
        public static readonly Regex CommandParser = new("[A-Z,a-z]\\s?(\\-?\\d+\\.?\\d?\\s?\\,?)+", RegexOptions.Compiled);
        
        public SVGPath(string pathElement) {
            // obtain the dElement
            string dElement = DElementParser.Match(pathElement).Value;
            
            // parse the dElement 
            foreach (Match match in CommandParser.Matches(dElement)) {
                (SVGCommand.SVGCommandType? commandType, bool relative) = SVGCommand.ParseCommand(match.Value[0]);
                if (commandType == null) {
                    throw new InvalidDataException($"Malformed path: {dElement}; unknown command: {match.Value[0]}");
                }
                string[] values = match.Value[1..].Split(' ', ',');
                List<float> valuesList = new List<float>();
                foreach (string value in values) {
                    if (value == "") continue;
                    if (!float.TryParse(value, out float parsed)) {
                        throw new InvalidDataException($"Malformed number in path: {dElement}; at: {value}");
                    }
                    valuesList.Add(parsed);
                }
                Commands.Add(new SVGCommand(commandType.Value, relative, valuesList)); 
                // - commandType.Value: above, dotnet wraps the type with Nullable<> so we have to access the value like this
            }
        }

    }
    public class SVGCommand {
        public readonly SVGCommandType Type;
        public readonly bool Relative;
        public readonly IEnumerable<float> Values;

        public SVGCommand(SVGCommandType type, bool relative, IEnumerable<float> values) {
            Type = type;
            Values = values;
            Relative = relative;
        }

        public enum SVGCommandType {
            MoveTo,
            LineTo,
            CubicCurve,
            QuadraticCurve,
            ArcCurve,
            ClosePath,
        }

        public static (SVGCommandType?, bool) ParseCommand(char command) {
            return command switch {
                'M' or 'm' => (SVGCommandType.MoveTo, command == 'm'),
                'L' or 'l' => (SVGCommandType.LineTo, command == 'l'),
                'C' or 'c' => (SVGCommandType.CubicCurve, command == 'c'),
                'Q' or 'q' => (SVGCommandType.QuadraticCurve, command == 'q'),
                'A' or 'a' => (SVGCommandType.ArcCurve, command == 'a'),
                'Z' or 'z' => (SVGCommandType.ClosePath, command == 'z'),
                _ => (null, false)
            };
        }
            
    }
    public enum SVGStrokeLineCap {
        Butt,
        Round,
        Square,
    }
}