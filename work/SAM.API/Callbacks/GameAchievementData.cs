/* Copyright (c) 2024 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SAM.API.Callbacks
{
    public class GameAchievementData : Callback<Types.GameAchievementData>
    {
        public override int Id => 1102;
        public override bool IsServer => false;

        /// <summary>
        /// Reads the raw (name, percent) buffer into a dictionary.
        /// </summary>
        public static Dictionary<string, double> ReadPercentages(Types.GameAchievementData data)
        {
            var percentages = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            if (data.Data == IntPtr.Zero || data.Count <= 0)
            {
                return percentages;
            }

            long address = data.Data.ToInt64();
            for (int i = 0; i < data.Count; i++)
            {
                IntPtr namePointer = new(address);
                string name = NativeStrings.PointerToString(namePointer);
                address += name.Length + 1;

                // unaligned doubles have to be read by hand
                ulong raw = (ulong)Marshal.ReadInt32(new IntPtr(address)) << 32;
                raw |= (uint)Marshal.ReadInt32(new IntPtr(address + 4));
                address += 8;

                if (string.IsNullOrEmpty(name) == true)
                {
                    continue;
                }

                percentages[name] = BitConverter.Int64BitsToDouble((long)raw);
            }

            return percentages;
        }
    }
}
