using System;
using System.Runtime.InteropServices;

namespace MCServerLauncher.WPF.Modules
{
    public static class Clipboard
    {
        [DllImport("User32")]
        public static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("User32")]
        public static extern bool CloseClipboard();

        [DllImport("User32")]
        public static extern bool EmptyClipboard();

        [DllImport("User32")]
        public static extern bool IsClipboardFormatAvailable(int format);

        [DllImport("User32")]
        public static extern IntPtr GetClipboardData(int uFormat);

        [DllImport("User32", CharSet = CharSet.Unicode)]
        public static extern IntPtr SetClipboardData(int uFormat, IntPtr hMem);

        /// <summary>
        ///     向剪贴板中添加文本。
        /// </summary>
        /// <remarks>
        ///     <list type="bullet">
        ///         <item>
        ///             <term>Q:</term>
        ///             <description>为什么不用原版的 <c>Clipboard.SetText(...)</c>?</description>
        ///         </item>
        ///         <item>
        ///             <term>A:</term>
        ///             <description>
        ///                 某些软件占用剪贴板，导致 <c>Clipboard.SetText(...)</c> 无法写入：
        ///                 <para>
        ///                     <c>
        ///                         System.Runtime.InteropServices.COMException (0x800401D0): OpenClipboard failed (0x800401D0
        ///                         (CLIPBRD_E_CANT_OPEN))。
        ///                     </c>
        ///                 </para>
        ///                 因此使用 WinAPI 来实现。
        ///             </description>
        ///         </item>
        ///     </list>
        /// </remarks>
        /// <param name="text">文本</param>
        public static void SetText(string text)
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                SetText(text);
                return;
            }

            EmptyClipboard();
            SetClipboardData(ClipboardFormat.CF_UNICODETEXT, Marshal.StringToHGlobalUni(text));
            CloseClipboard();
        }
    }

    public static class ClipboardFormat
    {
        /// <summary>
        ///     Text format. Each line ends with a carriage return/linefeed (CR-LF) combination. A null character signals
        ///     the end of the data. Use this format for ANSI text.
        /// </summary>
        public const int CF_TEXT = 1;

        /// <summary>
        ///     A handle to a bitmap (<c>HBITMAP</c>).
        /// </summary>
        public const int CF_BITMAP = 2;

        /// <summary>
        ///     Handle to a metafile picture format as defined by the <c>METAFILEPICT</c> structure. When passing a
        ///     <c>CF_METAFILEPICT</c> handle by means of DDE, the application responsible for deleting <c>hMem</c> should
        ///     also free the metafile referred to by the <c>CF_METAFILEPICT</c> handle.
        /// </summary>
        public const int CF_METAFILEPICT = 3;

        /// <summary>
        ///     Microsoft Symbolic Link (SYLK) format.
        /// </summary>
        public const int CF_SYLK = 4;

        /// <summary>
        ///     Software Arts' Data Interchange Format.
        /// </summary>
        public const int CF_DIF = 5;

        /// <summary>
        ///     Tagged-image file format.
        /// </summary>
        public const int CF_TIFF = 6;

        /// <summary>
        ///     Text format containing characters in the OEM character set. Each line ends with a carriage return/linefeed
        ///     (CR-LF) combination. A null character signals the end of the data.
        /// </summary>
        public const int CF_OEMTEXT = 7;

        /// <summary>
        ///     A memory object containing a <c>BITMAPINFO</c> structure followed by the bitmap bits.
        /// </summary>
        public const int CF_DIB = 8;

        /// <summary>
        ///     Handle to a color palette. Whenever an application places data in the clipboard that depends on or assumes
        ///     a color palette, it should place the palette on the clipboard as well. If the clipboard contains data in
        ///     the <see cref="CF_PALETTE" /> (logical color palette) format, the application should use the
        ///     <c>SelectPalette</c> and <c>RealizePalette</c> functions to realize (compare) any other data in the
        ///     clipboard against that logical palette. When displaying clipboard data, the clipboard always uses as its
        ///     current palette any object on the clipboard that is in the <c>CF_PALETTE</c> format.
        /// </summary>
        public const int CF_PALETTE = 9;

        /// <summary>
        ///     Data for the pen extensions to the Microsoft Windows for Pen Computing.
        /// </summary>
        public const int CF_PENDATA = 10;

        /// <summary>
        ///     Represents audio data more complex than can be represented in a CF_WAVE standard wave format.
        /// </summary>
        public const int CF_RIFF = 11;

        /// <summary>
        ///     Represents audio data in one of the standard wave formats, such as 11 kHz or 22 kHz PCM.
        /// </summary>
        public const int CF_WAVE = 12;

        /// <summary>
        ///     Unicode text format. Each line ends with a carriage return/linefeed (CR-LF) combination. A null character
        ///     signals the end of the data.
        /// </summary>
        public const int CF_UNICODETEXT = 13;

        /// <summary>
        ///     A handle to an enhanced metafile (<c>HENHMETAFILE</c>).
        /// </summary>
        public const int CF_ENHMETAFILE = 14;

        /// <summary>
        ///     A handle to type <c>HDROP</c> that identifies a list of files. An application can retrieve information
        ///     about the files by passing the handle to the <c>DragQueryFile</c> function.
        /// </summary>
        public const int CF_HDROP = 15;

        /// <summary>
        ///     The data is a handle to the locale identifier associated with text in the clipboard. When you close the
        ///     clipboard, if it contains <c>CF_TEXT</c> data but no <c>CF_LOCALE</c> data, the system automatically sets
        ///     the <c>CF_LOCALE</c> format to the current input language. You can use the <c>CF_LOCALE</c> format to
        ///     associate a different locale with the clipboard text.
        ///     An application that pastes text from the clipboard can retrieve this format to determine which character
        ///     set was used to generate the text.
        ///     Note that the clipboard does not support plain text in multiple character sets. To achieve this, use a
        ///     formatted text data type such as RTF instead.
        ///     The system uses the code page associated with <c>CF_LOCALE</c> to implicitly convert from
        ///     <see cref="CF_TEXT" /> to <see cref="CF_UNICODETEXT" />. Therefore, the correct code page table is used for
        ///     the conversion.
        /// </summary>
        public const int CF_LOCALE = 16;

        /// <summary>
        ///     A memory object containing a <c>BITMAPV5HEADER</c> structure followed by the bitmap color space
        ///     information and the bitmap bits.
        /// </summary>
        public const int CF_DIBV5 = 17;

        /// <summary>
        ///     Owner-display format. The clipboard owner must display and update the clipboard viewer window, and receive
        ///     the <see cref="ClipboardMessages.WM_ASKCBFORMATNAME" />, <see cref="ClipboardMessages.WM_HSCROLLCLIPBOARD" />,
        ///     <see cref="ClipboardMessages.WM_PAINTCLIPBOARD" />, <see cref="ClipboardMessages.WM_SIZECLIPBOARD" />, and
        ///     <see cref="ClipboardMessages.WM_VSCROLLCLIPBOARD" /> messages. The <c>hMem</c> parameter must be <c>null</c>.
        /// </summary>
        public const int CF_OWNERDISPLAY = 0x0080;

        /// <summary>
        ///     Text display format associated with a private format. The <c>hMem</c> parameter must be a handle to data
        ///     that can be displayed in text format in lieu of the privately formatted data.
        /// </summary>
        public const int CF_DSPTEXT = 0x0081;

        /// <summary>
        ///     Bitmap display format associated with a private format. The <c>hMem</c> parameter must be a handle to
        ///     data that can be displayed in bitmap format in lieu of the privately formatted data.
        /// </summary>
        public const int CF_DSPBITMAP = 0x0082;

        /// <summary>
        ///     Metafile-picture display format associated with a private format. The <c>hMem</c> parameter must be a
        ///     handle to data that can be displayed in metafile-picture format in lieu of the privately formatted data.
        /// </summary>
        public const int CF_DSPMETAFILEPICT = 0x0083;

        /// <summary>
        ///     Enhanced metafile display format associated with a private format. The <c>hMem</c> parameter must be a
        ///     handle to data that can be displayed in enhanced metafile format in lieu of the privately formatted data.
        /// </summary>
        public const int CF_DSPENHMETAFILE = 0x008E;

        /// <summary>
        ///     Start of a range of integer values for application-defined GDI object clipboard formats. The end of the
        ///     range is <see cref="CF_GDIOBJLAST" />. Handles associated with clipboard formats in this range are not
        ///     automatically deleted using the <c>GlobalFree</c> function when the clipboard is emptied. Also, when using
        ///     values in this range, the <c>hMem</c> parameter is not a handle to a GDI object, but is a handle allocated
        ///     by the <c>GlobalAlloc</c> function with the <c>GMEM_MOVEABLE</c> flag.
        /// </summary>
        public const int CF_GDIOBJFIRST = 0x0300;

        /// <summary>
        ///     See <see cref="CF_GDIOBJFIRST" />.
        /// </summary>
        public const int CF_GDIOBJLAST = 0x03FF;

        /// <summary>
        ///     Start of a range of integer values for private clipboard formats. The range ends with
        ///     <see cref="CF_PRIVATELAST" />. Handles associated with private clipboard formats are not freed
        ///     automatically; the clipboard owner must free such handles, typically in response to the
        ///     <see cref="ClipboardMessages.WM_DESTROYCLIPBOARD" /> message.
        /// </summary>
        public const int CF_PRIVATEFIRST = 0x0200;

        /// <summary>
        ///     See <see cref="CF_PRIVATEFIRST" />.
        /// </summary>
        public const int CF_PRIVATELAST = 0x02FF;
    }
}