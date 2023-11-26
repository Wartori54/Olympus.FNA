using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Olympus.Finders {
    public unsafe class UWPFinder : Finder {
        protected override Installation.InstallationType InstallationType => Installation.InstallationType.UWP;

        public UWPFinder(FinderManager manager)
            : base(manager) {
        }

#pragma warning disable CS1998 // Async without await because this finder can't await. Async, enumerators and unsafe hate each other.
        private static async IAsyncEnumerable<Installation> Result() {
            yield break;
        }

        private static async IAsyncEnumerable<Installation> Result(Installation install) {
            yield return install;
        }
#pragma warning restore CS1998

        public override IAsyncEnumerable<Installation> FindCandidates() {
            if (!PlatformHelper.Is(Platform.Windows))
                return Result();

            // https://www.microsoft.com/en-us/p/celeste/bwmql2rpwbhb
            // https://bspmts.mp.microsoft.com/v1/public/catalog/Retail/Products/bwmql2rpwbhb/applockerdata
            string package = "MattMakesGamesInc.Celeste_79daxvg0dq3v6";

            IntPtr buffer = IntPtr.Zero;
            try {
                uint count;
                uint bufferLength;
                Error status;


                // family -> full name

                count = 0;
                bufferLength = 0;
                status = GetPackagesByPackageFamily(package, ref count, (char**) 0, ref bufferLength, (char*) 0);
                if (count == 0 || bufferLength == 0 || (status != Error.Success && status != Error.InsufficientBuffer))
                    return Result();

                char*[]? packageFullNames = new char*[count];
                buffer = Marshal.AllocHGlobal((int) bufferLength * sizeof(char));

                fixed (char** packageFullNamesPtr = packageFullNames)
                    status = GetPackagesByPackageFamily(package, ref count, packageFullNamesPtr, ref bufferLength, (char*) buffer);
                if (status != Error.Success)
                    return Result();

                // Only the first full package name is required anyway.
                package = new string(packageFullNames[0]);
                packageFullNames = null;
                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;


                // full name -> path

                bufferLength = 0;
                status = GetPackagePathByFullName(package, ref bufferLength, (char*) 0);
                if (bufferLength == 0 || (status != Error.Success && status != Error.InsufficientBuffer))
                    return Result();

                buffer = Marshal.AllocHGlobal((int) bufferLength * sizeof(char));
                status = GetPackagePathByFullName(package, ref bufferLength, (char*) buffer);
                if (status != Error.Success)
                    return Result();

                package = new string((char*) buffer);
                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;


                return Result(new(InstallationType, "Microsoft Store", package));

            } catch {
                return Result();

            } finally {
                if (buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(buffer);
            }
        }

        public enum Error : long {
            Success = 0x00,
            InsufficientBuffer = 0x7A
        }

        public enum PackagePathType : long {
            Install,
            Mutable,
            Effective,
            MachineExternal,
            UserExternal,
            EffectiveExternal
        }

        [DllImport("kernel32")]
        public static extern Error GetPackagesByPackageFamily(
            [MarshalAs(UnmanagedType.LPWStr)] string packageFamilyName,
            ref uint count,
            char** packageFullNames,
            ref uint bufferLength,
            char* buffer
        );

        [DllImport("kernel32")]
        public static extern Error GetPackagePathByFullName(
            [MarshalAs(UnmanagedType.LPWStr)] string packageFullName,
            ref uint pathLength,
            char* path
        );

    }
}
