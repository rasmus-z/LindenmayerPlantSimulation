﻿using System.Runtime.InteropServices;
using UnityEngine;

namespace Dman.LSystem.SystemRuntime.Turtle
{
    [StructLayout(LayoutKind.Explicit)]
    public struct UIntFloatColor32
    {
        [FieldOffset(0)]
        public uint UIntValue;
        [FieldOffset(0)]
        public float FloatValue;
        [FieldOffset(0)]
        public Color32 color;
        public UIntFloatColor32(Color32 value)
        {
            UIntValue = default;
            FloatValue = default;

            color = value;

        }
        public UIntFloatColor32(float value)
        {
            UIntValue = default;
            color = default;

            FloatValue = value;
        }
        public UIntFloatColor32(uint value)
        {
            FloatValue = default;
            color = default;

            UIntValue = value;
        }
        public static uint AsUint(Color32 color)
        {
            return new UIntFloatColor32 { color = color }.UIntValue;
        }
        public static Color32 AsColor32(uint uInt)
        {
            return new UIntFloatColor32 { UIntValue = uInt }.color;
        }
    }

}
