# Third-party notices

Original OmniLux code, artwork, and documentation are MIT licensed; see `LICENSE`.
The UI design is informed by [OmniSound](https://github.com/jarrodhwang/OmniSound),
Copyright (c) 2026 Jarrod Hwang, MIT. No OmniSound driver or audio component is bundled.

Self-contained packages include Microsoft's .NET 10 and Windows Desktop runtime.
These components retain their upstream MIT license and third-party notices:

- [.NET runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)
- [.NET runtime third-party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT)
- [WPF license](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT)
- [Windows Forms license](https://github.com/dotnet/winforms/blob/main/LICENSE.TXT)

Full runtime licenses and third-party notices from the restored runtime packs are
preserved under `licenses/` in each self-contained package. Windows system DLLs and fonts are used from the user's
Windows installation and are not redistributed. OmniLux uses only Windows' brightness
APIs and does not ship a third-party DDC/CI library.

Inno Setup 7.1.0 is a build tool. Its portable compiler is downloaded from the
[official release](https://github.com/jrsoftware/issrc/releases/tag/is-7_1_0), verified,
and kept in an ignored cache. Its [upstream terms](https://github.com/jrsoftware/issrc/blob/is-7_1_0/license.txt)
continue to apply to Inno Setup components. The compiler is not included in OmniLux.
