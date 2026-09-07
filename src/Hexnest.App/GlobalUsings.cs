// The string table moved to Hexnest.Core when the Mac build was added: both
// applications display the same text in the same five languages, and two copies of
// it would have drifted apart within one release.
//
// It is imported globally rather than with a using line in each of the two dozen
// files that call Loc.T, so that the move cost the WPF application no diff at all.
global using Hexnest.Core.Localization;
