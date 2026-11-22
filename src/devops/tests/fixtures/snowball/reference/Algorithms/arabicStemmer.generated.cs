// Generated from arabic.sbl by Snowball 3.2.0 - https://snowballstem.org/

#pragma warning disable 0164
#pragma warning disable 0162

namespace Snowball
{
    using System;
    using System.Text;

    ///<summary>
    ///  This class implements the stemming algorithm defined by a snowball script.
    ///  Generated from arabic.sbl by Snowball 3.2.0 - https://snowballstem.org/
    ///</summary>
    ///
    [System.CodeDom.Compiler.GeneratedCode("Snowball", "3.2.0")]
    public partial class ArabicStemmer : Stemmer
    {

        private static readonly Among[] a_0 = new[]
        {
            new Among("\u0640", -1, 1, 0),
            new Among("\u064B", -1, 1, 0),
            new Among("\u064C", -1, 1, 0),
            new Among("\u064D", -1, 1, 0),
            new Among("\u064E", -1, 1, 0),
            new Among("\u064F", -1, 1, 0),
            new Among("\u0650", -1, 1, 0),
            new Among("\u0651", -1, 1, 0),
            new Among("\u0652", -1, 1, 0),
            new Among("\u0660", -1, 2, 0),
            new Among("\u0661", -1, 3, 0),
            new Among("\u0662", -1, 4, 0),
            new Among("\u0663", -1, 5, 0),
            new Among("\u0664", -1, 6, 0),
            new Among("\u0665", -1, 7, 0),
            new Among("\u0666", -1, 8, 0),
            new Among("\u0667", -1, 9, 0),
            new Among("\u0668", -1, 10, 0),
            new Among("\u0669", -1, 11, 0),
            new Among("\uFE80", -1, 12, 0),
            new Among("\uFE81", -1, 16, 0),
            new Among("\uFE82", -1, 16, 0),
            new Among("\uFE83", -1, 13, 0),
            new Among("\uFE84", -1, 13, 0),
            new Among("\uFE85", -1, 17, 0),
            new Among("\uFE86", -1, 17, 0),
            new Among("\uFE87", -1, 14, 0),
            new Among("\uFE88", -1, 14, 0),
            new Among("\uFE89", -1, 15, 0),
            new Among("\uFE8A", -1, 15, 0),
            new Among("\uFE8B", -1, 15, 0),
            new Among("\uFE8C", -1, 15, 0),
            new Among("\uFE8D", -1, 18, 0),
            new Among("\uFE8E", -1, 18, 0),
            new Among("\uFE8F", -1, 19, 0),
            new Among("\uFE90", -1, 19, 0),
            new Among("\uFE91", -1, 19, 0),
            new Among("\uFE92", -1, 19, 0),
            new Among("\uFE93", -1, 20, 0),
            new Among("\uFE94", -1, 20, 0),
            new Among("\uFE95", -1, 21, 0),
            new Among("\uFE96", -1, 21, 0),
            new Among("\uFE97", -1, 21, 0),
            new Among("\uFE98", -1, 21, 0),
            new Among("\uFE99", -1, 22, 0),
            new Among("\uFE9A", -1, 22, 0),
            new Among("\uFE9B", -1, 22, 0),
            new Among("\uFE9C", -1, 22, 0),
            new Among("\uFE9D", -1, 23, 0),
            new Among("\uFE9E", -1, 23, 0),
            new Among("\uFE9F", -1, 23, 0),
            new Among("\uFEA0", -1, 23, 0),
            new Among("\uFEA1", -1, 24, 0),
            new Among("\uFEA2", -1, 24, 0),
            new Among("\uFEA3", -1, 24, 0),
            new Among("\uFEA4", -1, 24, 0),
            new Among("\uFEA5", -1, 25, 0),
            new Among("\uFEA6", -1, 25, 0),
            new Among("\uFEA7", -1, 25, 0),
            new Among("\uFEA8", -1, 25, 0),
            new Among("\uFEA9", -1, 26, 0),
            new Among("\uFEAA", -1, 26, 0),
            new Among("\uFEAB", -1, 27, 0),
            new Among("\uFEAC", -1, 27, 0),
            new Among("\uFEAD", -1, 28, 0),
            new Among("\uFEAE", -1, 28, 0),
            new Among("\uFEAF", -1, 29, 0),
            new Among("\uFEB0", -1, 29, 0),
            new Among("\uFEB1", -1, 30, 0),
            new Among("\uFEB2", -1, 30, 0),
            new Among("\uFEB3", -1, 30, 0),
            new Among("\uFEB4", -1, 30, 0),
            new Among("\uFEB5", -1, 31, 0),
            new Among("\uFEB6", -1, 31, 0),
            new Among("\uFEB7", -1, 31, 0),
            new Among("\uFEB8", -1, 31, 0),
            new Among("\uFEB9", -1, 32, 0),
            new Among("\uFEBA", -1, 32, 0),
            new Among("\uFEBB", -1, 32, 0),
            new Among("\uFEBC", -1, 32, 0),
            new Among("\uFEBD", -1, 33, 0),
            new Among("\uFEBE", -1, 33, 0),
            new Among("\uFEBF", -1, 33, 0),
            new Among("\uFEC0", -1, 33, 0),
            new Among("\uFEC1", -1, 34, 0),
            new Among("\uFEC2", -1, 34, 0),
            new Among("\uFEC3", -1, 34, 0),
            new Among("\uFEC4", -1, 34, 0),
            new Among("\uFEC5", -1, 35, 0),
            new Among("\uFEC6", -1, 35, 0),
            new Among("\uFEC7", -1, 35, 0),
            new Among("\uFEC8", -1, 35, 0),
            new Among("\uFEC9", -1, 36, 0),
            new Among("\uFECA", -1, 36, 0),
            new Among("\uFECB", -1, 36, 0),
            new Among("\uFECC", -1, 36, 0),
            new Among("\uFECD", -1, 37, 0),
            new Among("\uFECE", -1, 37, 0),
            new Among("\uFECF", -1, 37, 0),
            new Among("\uFED0", -1, 37, 0),
            new Among("\uFED1", -1, 38, 0),
            new Among("\uFED2", -1, 38, 0),
            new Among("\uFED3", -1, 38, 0),
            new Among("\uFED4", -1, 38, 0),
            new Among("\uFED5", -1, 39, 0),
            new Among("\uFED6", -1, 39, 0),
            new Among("\uFED7", -1, 39, 0),
            new Among("\uFED8", -1, 39, 0),
            new Among("\uFED9", -1, 40, 0),
            new Among("\uFEDA", -1, 40, 0),
            new Among("\uFEDB", -1, 40, 0),
            new Among("\uFEDC", -1, 40, 0),
            new Among("\uFEDD", -1, 41, 0),
            new Among("\uFEDE", -1, 41, 0),
            new Among("\uFEDF", -1, 41, 0),
            new Among("\uFEE0", -1, 41, 0),
            new Among("\uFEE1", -1, 42, 0),
            new Among("\uFEE2", -1, 42, 0),
            new Among("\uFEE3", -1, 42, 0),
            new Among("\uFEE4", -1, 42, 0),
            new Among("\uFEE5", -1, 43, 0),
            new Among("\uFEE6", -1, 43, 0),
            new Among("\uFEE7", -1, 43, 0),
            new Among("\uFEE8", -1, 43, 0),
            new Among("\uFEE9", -1, 44, 0),
            new Among("\uFEEA", -1, 44, 0),
            new Among("\uFEEB", -1, 44, 0),
            new Among("\uFEEC", -1, 44, 0),
            new Among("\uFEED", -1, 45, 0),
            new Among("\uFEEE", -1, 45, 0),
            new Among("\uFEEF", -1, 46, 0),
            new Among("\uFEF0", -1, 46, 0),
            new Among("\uFEF1", -1, 47, 0),
            new Among("\uFEF2", -1, 47, 0),
            new Among("\uFEF3", -1, 47, 0),
            new Among("\uFEF4", -1, 47, 0),
            new Among("\uFEF5", -1, 51, 0),
            new Among("\uFEF6", -1, 51, 0),
            new Among("\uFEF7", -1, 49, 0),
            new Among("\uFEF8", -1, 49, 0),
            new Among("\uFEF9", -1, 50, 0),
            new Among("\uFEFA", -1, 50, 0),
            new Among("\uFEFB", -1, 48, 0),
            new Among("\uFEFC", -1, 48, 0)
        };

        private static readonly Among[] a_1 = new[]
        {
            new Among("\u0622", -1, 1, 0),
            new Among("\u0623", -1, 1, 0),
            new Among("\u0624", -1, 1, 0),
            new Among("\u0625", -1, 1, 0),
            new Among("\u0626", -1, 1, 0)
        };

        private static readonly Among[] a_2 = new[]
        {
            new Among("\u0622", -1, 1, 0),
            new Among("\u0623", -1, 1, 0),
            new Among("\u0624", -1, 2, 0),
            new Among("\u0625", -1, 1, 0),
            new Among("\u0626", -1, 3, 0)
        };

        private static readonly Among[] a_3 = new[]
        {
            new Among("\u0627\u0644", -1, 2, 0),
            new Among("\u0628\u0627\u0644", -1, 1, 0),
            new Among("\u0643\u0627\u0644", -1, 1, 0),
            new Among("\u0644\u0644", -1, 2, 0)
        };

        private static readonly Among[] a_4 = new[]
        {
            new Among("\u0623\u0622", -1, 2, 0),
            new Among("\u0623\u0623", -1, 1, 0),
            new Among("\u0623\u0624", -1, 1, 0),
            new Among("\u0623\u0625", -1, 4, 0),
            new Among("\u0623\u0627", -1, 3, 0)
        };

        private static readonly Among[] a_5 = new[]
        {
            new Among("\u0641", -1, 1, 0),
            new Among("\u0648", -1, 1, 0)
        };

        private static readonly Among[] a_6 = new[]
        {
            new Among("\u0627\u0644", -1, 2, 0),
            new Among("\u0628\u0627\u0644", -1, 1, 0),
            new Among("\u0643\u0627\u0644", -1, 1, 0),
            new Among("\u0644\u0644", -1, 2, 0)
        };

        private static readonly Among[] a_7 = new[]
        {
            new Among("\u0628", -1, 1, 0),
            new Among("\u0628\u0627", 0, -1, 0),
            new Among("\u0628\u0628", 0, 2, 0),
            new Among("\u0643\u0643", -1, 3, 0)
        };

        private static readonly Among[] a_8 = new[]
        {
            new Among("\u0633\u0623", -1, 4, 0),
            new Among("\u0633\u062A", -1, 2, 0),
            new Among("\u0633\u0646", -1, 3, 0),
            new Among("\u0633\u064A", -1, 1, 0)
        };

        private static readonly Among[] a_9 = new[]
        {
            new Among("\u062A\u0633\u062A", -1, 1, 0),
            new Among("\u0646\u0633\u062A", -1, 1, 0),
            new Among("\u064A\u0633\u062A", -1, 1, 0)
        };

        private static readonly Among[] a_10 = new[]
        {
            new Among("\u0643\u0645\u0627", -1, 3, 0),
            new Among("\u0647\u0645\u0627", -1, 3, 0),
            new Among("\u0646\u0627", -1, 2, 0),
            new Among("\u0647\u0627", -1, 2, 0),
            new Among("\u0643", -1, 1, 0),
            new Among("\u0643\u0645", -1, 2, 0),
            new Among("\u0647\u0645", -1, 2, 0),
            new Among("\u0647\u0646", -1, 2, 0),
            new Among("\u0647", -1, 1, 0),
            new Among("\u064A", -1, 1, 0)
        };

        private static readonly Among[] a_11 = new[]
        {
            new Among("\u0627", -1, 1, 0),
            new Among("\u0648", -1, 1, 0),
            new Among("\u064A", -1, 1, 0)
        };

        private static readonly Among[] a_12 = new[]
        {
            new Among("\u0643\u0645\u0627", -1, 3, 0),
            new Among("\u0647\u0645\u0627", -1, 3, 0),
            new Among("\u0646\u0627", -1, 2, 0),
            new Among("\u0647\u0627", -1, 2, 0),
            new Among("\u0643", -1, 1, 0),
            new Among("\u0643\u0645", -1, 2, 0),
            new Among("\u0647\u0645", -1, 2, 0),
            new Among("\u0643\u0646", -1, 2, 0),
            new Among("\u0647\u0646", -1, 2, 0),
            new Among("\u0647", -1, 1, 0),
            new Among("\u0643\u0645\u0648", -1, 3, 0),
            new Among("\u0646\u064A", -1, 2, 0)
        };

        private static readonly Among[] a_13 = new[]
        {
            new Among("\u0627", -1, 1, 0),
            new Among("\u062A\u0627", 0, 2, 0),
            new Among("\u062A\u0645\u0627", 0, 3, 0),
            new Among("\u0646\u0627", 0, 2, 0),
            new Among("\u062A", -1, 1, 0),
            new Among("\u0646", -1, 1, 0),
            new Among("\u0627\u0646", 5, 3, 0),
            new Among("\u062A\u0646", 5, 2, 0),
            new Among("\u0648\u0646", 5, 3, 0),
            new Among("\u064A\u0646", 5, 3, 0),
            new Among("\u064A", -1, 1, 0)
        };

        private static readonly Among[] a_14 = new[]
        {
            new Among("\u0648\u0627", -1, 1, 0),
            new Among("\u062A\u0645", -1, 1, 0)
        };

        private static readonly Among[] a_15 = new[]
        {
            new Among("\u0648", -1, 1, 0),
            new Among("\u062A\u0645\u0648", 0, 2, 0)
        };


        private bool r_Suffix_Noun_Step2a()
        {
            ket = cursor;
            if (find_among_b(a_11, null) == 0)
            {
                return false;
            }
            bra = cursor;
            if (current.Length < 5)
            {
                return false;
            }
            slice_del();
            return true;
        }

        private bool r_Suffix_Noun_Step2b()
        {
            ket = cursor;
            if (!(eq_s_b("\u0627\u062A")))
            {
                return false;
            }
            bra = cursor;
            if (current.Length < 5)
            {
                return false;
            }
            slice_del();
            return true;
        }

        private bool r_Suffix_Noun_Step2c1()
        {
            ket = cursor;
            if (!(eq_s_b("\u062A")))
            {
                return false;
            }
            bra = cursor;
            if (current.Length < 4)
            {
                return false;
            }
            slice_del();
            return true;
        }

        private bool r_Suffix_Verb_Step2a()
        {
            int among_var;
            ket = cursor;
            among_var = find_among_b(a_13, null);
            if (among_var == 0)
            {
                return false;
            }
            bra = cursor;
            switch (among_var) {
                case 1: {
                    if (current.Length < 4)
                    {
                        return false;
                    }
                    slice_del();
                    break;
                }
                case 2: {
                    if (current.Length < 5)
                    {
                        return false;
                    }
                    slice_del();
                    break;
                }
                case 3: {
                    if (current.Length < 6)
                    {
                        return false;
                    }
                    slice_del();
                    break;
                }
            }
            return true;
        }

        protected override bool stem()
        {
            int among_var;
            bool B_is_defined;
            bool B_is_verb;
            bool B_is_noun;
            B_is_noun = true;
            B_is_verb = true;
            B_is_defined = false;
            {
                int c1 = cursor;
                bra = cursor;
                among_var = find_among(a_3, null);
                if (among_var == 0)
                {
                    goto lab0;
                }
                ket = cursor;
                switch (among_var) {
                    case 1: {
                        if (current.Length < 5)
                        {
                            goto lab0;
                        }
                        B_is_noun = true;
                        B_is_verb = false;
                        B_is_defined = true;
                        break;
                    }
                    case 2: {
                        if (current.Length < 4)
                        {
                            goto lab0;
                        }
                        B_is_noun = true;
                        B_is_verb = false;
                        B_is_defined = true;
                        break;
                    }
                }
            lab0: ;
                cursor = c1;
            }
            {
                int c2 = cursor;
                while (true)
                {
                    int c3 = cursor;
                    {
                        int c4 = cursor;
                        bra = cursor;
                        among_var = find_among(a_0, null);
                        if (among_var == 0)
                        {
                            goto lab5;
                        }
                        ket = cursor;
                        switch (among_var) {
                            case 1: {
                                slice_del();
                                break;
                            }
                            case 2: {
                                slice_from("0");
                                break;
                            }
                            case 3: {
                                slice_from("1");
                                break;
                            }
                            case 4: {
                                slice_from("2");
                                break;
                            }
                            case 5: {
                                slice_from("3");
                                break;
                            }
                            case 6: {
                                slice_from("4");
                                break;
                            }
                            case 7: {
                                slice_from("5");
                                break;
                            }
                            case 8: {
                                slice_from("6");
                                break;
                            }
                            case 9: {
                                slice_from("7");
                                break;
                            }
                            case 10: {
                                slice_from("8");
                                break;
                            }
                            case 11: {
                                slice_from("9");
                                break;
                            }
                            case 12: {
                                slice_from("\u0621");
                                break;
                            }
                            case 13: {
                                slice_from("\u0623");
                                break;
                            }
                            case 14: {
                                slice_from("\u0625");
                                break;
                            }
                            case 15: {
                                slice_from("\u0626");
                                break;
                            }
                            case 16: {
                                slice_from("\u0622");
                                break;
                            }
                            case 17: {
                                slice_from("\u0624");
                                break;
                            }
                            case 18: {
                                slice_from("\u0627");
                                break;
                            }
                            case 19: {
                                slice_from("\u0628");
                                break;
                            }
                            case 20: {
                                slice_from("\u0629");
                                break;
                            }
                            case 21: {
                                slice_from("\u062A");
                                break;
                            }
                            case 22: {
                                slice_from("\u062B");
                                break;
                            }
                            case 23: {
                                slice_from("\u062C");
                                break;
                            }
                            case 24: {
                                slice_from("\u062D");
                                break;
                            }
                            case 25: {
                                slice_from("\u062E");
                                break;
                            }
                            case 26: {
                                slice_from("\u062F");
                                break;
                            }
                            case 27: {
                                slice_from("\u0630");
                                break;
                            }
                            case 28: {
                                slice_from("\u0631");
                                break;
                            }
                            case 29: {
                                slice_from("\u0632");
                                break;
                            }
                            case 30: {
                                slice_from("\u0633");
                                break;
                            }
                            case 31: {
                                slice_from("\u0634");
                                break;
                            }
                            case 32: {
                                slice_from("\u0635");
                                break;
                            }
                            case 33: {
                                slice_from("\u0636");
                                break;
                            }
                            case 34: {
                                slice_from("\u0637");
                                break;
                            }
                            case 35: {
                                slice_from("\u0638");
                                break;
                            }
                            case 36: {
                                slice_from("\u0639");
                                break;
                            }
                            case 37: {
                                slice_from("\u063A");
                                break;
                            }
                            case 38: {
                                slice_from("\u0641");
                                break;
                            }
                            case 39: {
                                slice_from("\u0642");
                                break;
                            }
                            case 40: {
                                slice_from("\u0643");
                                break;
                            }
                            case 41: {
                                slice_from("\u0644");
                                break;
                            }
                            case 42: {
                                slice_from("\u0645");
                                break;
                            }
                            case 43: {
                                slice_from("\u0646");
                                break;
                            }
                            case 44: {
                                slice_from("\u0647");
                                break;
                            }
                            case 45: {
                                slice_from("\u0648");
                                break;
                            }
                            case 46: {
                                slice_from("\u0649");
                                break;
                            }
                            case 47: {
                                slice_from("\u064A");
                                break;
                            }
                            case 48: {
                                slice_from("\u0644\u0627");
                                break;
                            }
                            case 49: {
                                slice_from("\u0644\u0623");
                                break;
                            }
                            case 50: {
                                slice_from("\u0644\u0625");
                                break;
                            }
                            case 51: {
                                slice_from("\u0644\u0622");
                                break;
                            }
                        }
                        goto lab4;
                    lab5: ;
                        cursor = c4;
                        if (cursor >= limit)
                        {
                            goto lab3;
                        }
                        cursor++;
                    }
                lab4: ;
                    continue;
                lab3: ;
                    cursor = c3;
                    break;
                }
                cursor = c2;
            }
            limit_backward = cursor;
            cursor = limit;
            {
                int c5 = limit - cursor;
                {
                    int c6 = limit - cursor;
                    if (!B_is_verb)
                    {
                        goto lab8;
                    }
                    {
                        int c7 = limit - cursor;
                        {
                            int c8 = 1;
                            while (true)
                            {
                                int c9 = limit - cursor;
                                ket = cursor;
                                among_var = find_among_b(a_12, null);
                                if (among_var == 0)
                                {
                                    goto lab11;
                                }
                                bra = cursor;
                                switch (among_var) {
                                    case 1: {
                                        if (current.Length < 4)
                                        {
                                            goto lab11;
                                        }
                                        slice_del();
                                        break;
                                    }
                                    case 2: {
                                        if (current.Length < 5)
                                        {
                                            goto lab11;
                                        }
                                        slice_del();
                                        break;
                                    }
                                    case 3: {
                                        if (current.Length < 6)
                                        {
                                            goto lab11;
                                        }
                                        slice_del();
                                        break;
                                    }
                                }
                                c8--;
                                continue;
                            lab11: ;
                                cursor = limit - c9;
                                break;
                            }
                            if (c8 > 0)
                            {
                                goto lab10;
                            }
                        }
                        {
                            int c10 = limit - cursor;
                            if (!r_Suffix_Verb_Step2a())
                                goto lab13;
                            goto lab12;
                        lab13: ;
                            cursor = limit - c10;
                            ket = cursor;
                            among_var = find_among_b(a_15, null);
                            if (among_var == 0)
                            {
                                goto lab14;
                            }
                            bra = cursor;
                            switch (among_var) {
                                case 1: {
                                    if (current.Length < 4)
                                    {
                                        goto lab14;
                                    }
                                    slice_del();
                                    break;
                                }
                                case 2: {
                                    if (current.Length < 6)
                                    {
                                        goto lab14;
                                    }
                                    slice_del();
                                    break;
                                }
                            }
                            goto lab12;
                        lab14: ;
                            cursor = limit - c10;
                            if (cursor <= limit_backward)
                            {
                                goto lab10;
                            }
                            cursor--;
                        }
                    lab12: ;
                        goto lab9;
                    lab10: ;
                        cursor = limit - c7;
                        ket = cursor;
                        if (find_among_b(a_14, null) == 0)
                        {
                            goto lab15;
                        }
                        bra = cursor;
                        if (current.Length < 5)
                        {
                            goto lab15;
                        }
                        slice_del();
                        goto lab9;
                    lab15: ;
                        cursor = limit - c7;
                        if (!r_Suffix_Verb_Step2a())
                            goto lab8;
                    }
                lab9: ;
                    goto lab7;
                lab8: ;
                    cursor = limit - c6;
                    if (!B_is_noun)
                    {
                        goto lab16;
                    }
                    {
                        int c11 = limit - cursor;
                        {
                            int c12 = limit - cursor;
                            ket = cursor;
                            if (!(eq_s_b("\u0629")))
                            {
                                goto lab19;
                            }
                            bra = cursor;
                            if (current.Length < 4)
                            {
                                goto lab19;
                            }
                            slice_del();
                            goto lab18;
                        lab19: ;
                            cursor = limit - c12;
                            if (B_is_defined)
                            {
                                goto lab20;
                            }
                            ket = cursor;
                            among_var = find_among_b(a_10, null);
                            if (among_var == 0)
                            {
                                goto lab20;
                            }
                            bra = cursor;
                            switch (among_var) {
                                case 1: {
                                    if (current.Length < 4)
                                    {
                                        goto lab20;
                                    }
                                    slice_del();
                                    break;
                                }
                                case 2: {
                                    if (current.Length < 5)
                                    {
                                        goto lab20;
                                    }
                                    slice_del();
                                    break;
                                }
                                case 3: {
                                    if (current.Length < 6)
                                    {
                                        goto lab20;
                                    }
                                    slice_del();
                                    break;
                                }
                            }
                            {
                                int c13 = limit - cursor;
                                if (!r_Suffix_Noun_Step2a())
                                    goto lab22;
                                goto lab21;
                            lab22: ;
                                cursor = limit - c13;
                                if (!r_Suffix_Noun_Step2b())
                                    goto lab23;
                                goto lab21;
                            lab23: ;
                                cursor = limit - c13;
                                if (!r_Suffix_Noun_Step2c1())
                                    goto lab24;
                                goto lab21;
                            lab24: ;
                                cursor = limit - c13;
                                if (cursor <= limit_backward)
                                {
                                    goto lab20;
                                }
                                cursor--;
                            }
                        lab21: ;
                            goto lab18;
                        lab20: ;
                            cursor = limit - c12;
                            ket = cursor;
                            if (!(eq_s_b("\u0646")))
                            {
                                goto lab25;
                            }
                            bra = cursor;
                            if (current.Length < 6)
                            {
                                goto lab25;
                            }
                            slice_del();
                            {
                                int c14 = limit - cursor;
                                if (!r_Suffix_Noun_Step2a())
                                    goto lab27;
                                goto lab26;
                            lab27: ;
                                cursor = limit - c14;
                                if (!r_Suffix_Noun_Step2b())
                                    goto lab28;
                                goto lab26;
                            lab28: ;
                                cursor = limit - c14;
                                if (!r_Suffix_Noun_Step2c1())
                                    goto lab25;
                            }
                        lab26: ;
                            goto lab18;
                        lab25: ;
                            cursor = limit - c12;
                            if (B_is_defined)
                            {
                                goto lab29;
                            }
                            if (!r_Suffix_Noun_Step2a())
                                goto lab29;
                            goto lab18;
                        lab29: ;
                            cursor = limit - c12;
                            if (!r_Suffix_Noun_Step2b())
                                {
                                    cursor = limit - c11;
                                    goto lab17;
                                }
                        }
                    lab18: ;
                    lab17: ;
                    }
                    ket = cursor;
                    if (!(eq_s_b("\u064A")))
                    {
                        goto lab16;
                    }
                    bra = cursor;
                    if (current.Length < 3)
                    {
                        goto lab16;
                    }
                    slice_del();
                    goto lab7;
                lab16: ;
                    cursor = limit - c6;
                    ket = cursor;
                    if (!(eq_s_b("\u0649")))
                    {
                        goto lab6;
                    }
                    bra = cursor;
                    slice_from("\u064A");
                }
            lab7: ;
            lab6: ;
                cursor = limit - c5;
            }
            cursor = limit_backward;
            {
                int c15 = cursor;
                {
                    int c16 = cursor;
                    bra = cursor;
                    among_var = find_among(a_4, null);
                    if (among_var == 0)
                    {
                        {
                            cursor = c16;
                            goto lab31;
                        }
                    }
                    ket = cursor;
                    switch (among_var) {
                        case 1: {
                            if (current.Length < 4)
                            {
                                {
                                    cursor = c16;
                                    goto lab31;
                                }
                            }
                            slice_from("\u0623");
                            break;
                        }
                        case 2: {
                            if (current.Length < 4)
                            {
                                {
                                    cursor = c16;
                                    goto lab31;
                                }
                            }
                            slice_from("\u0622");
                            break;
                        }
                        case 3: {
                            if (current.Length < 4)
                            {
                                {
                                    cursor = c16;
                                    goto lab31;
                                }
                            }
                            slice_from("\u0627");
                            break;
                        }
                        case 4: {
                            if (current.Length < 4)
                            {
                                {
                                    cursor = c16;
                                    goto lab31;
                                }
                            }
                            slice_from("\u0625");
                            break;
                        }
                    }
                lab31: ;
                }
                {
                    int c17 = cursor;
                    bra = cursor;
                    if (find_among(a_5, null) == 0)
                    {
                        {
                            cursor = c17;
                            goto lab32;
                        }
                    }
                    ket = cursor;
                    if (current.Length < 4)
                    {
                        {
                            cursor = c17;
                            goto lab32;
                        }
                    }
                    if (!(eq_s("\u0627")))
                    {
                        goto lab33;
                    }
                    {
                        cursor = c17;
                        goto lab32;
                    }
                lab33: ;
                    slice_del();
                lab32: ;
                }
                {
                    int c18 = cursor;
                    bra = cursor;
                    among_var = find_among(a_6, null);
                    if (among_var == 0)
                    {
                        goto lab35;
                    }
                    ket = cursor;
                    switch (among_var) {
                        case 1: {
                            if (current.Length < 6)
                            {
                                goto lab35;
                            }
                            slice_del();
                            break;
                        }
                        case 2: {
                            if (current.Length < 5)
                            {
                                goto lab35;
                            }
                            slice_del();
                            break;
                        }
                    }
                    goto lab34;
                lab35: ;
                    cursor = c18;
                    if (!B_is_noun)
                    {
                        goto lab36;
                    }
                    bra = cursor;
                    among_var = find_among(a_7, null);
                    if (among_var == 0)
                    {
                        goto lab36;
                    }
                    ket = cursor;
                    switch (among_var) {
                        case 1: {
                            if (current.Length < 4)
                            {
                                goto lab36;
                            }
                            slice_del();
                            break;
                        }
                        case 2: {
                            if (current.Length < 4)
                            {
                                goto lab36;
                            }
                            slice_from("\u0628");
                            break;
                        }
                        case 3: {
                            if (current.Length < 4)
                            {
                                goto lab36;
                            }
                            slice_from("\u0643");
                            break;
                        }
                    }
                    goto lab34;
                lab36: ;
                    cursor = c18;
                    if (!B_is_verb)
                    {
                        goto lab30;
                    }
                    {
                        int c19 = cursor;
                        bra = cursor;
                        among_var = find_among(a_8, null);
                        if (among_var == 0)
                        {
                            {
                                cursor = c19;
                                goto lab37;
                            }
                        }
                        ket = cursor;
                        switch (among_var) {
                            case 1: {
                                if (current.Length < 5)
                                {
                                    {
                                        cursor = c19;
                                        goto lab37;
                                    }
                                }
                                slice_from("\u064A");
                                break;
                            }
                            case 2: {
                                if (current.Length < 5)
                                {
                                    {
                                        cursor = c19;
                                        goto lab37;
                                    }
                                }
                                slice_from("\u062A");
                                break;
                            }
                            case 3: {
                                if (current.Length < 5)
                                {
                                    {
                                        cursor = c19;
                                        goto lab37;
                                    }
                                }
                                slice_from("\u0646");
                                break;
                            }
                            case 4: {
                                if (current.Length < 5)
                                {
                                    {
                                        cursor = c19;
                                        goto lab37;
                                    }
                                }
                                slice_from("\u0623");
                                break;
                            }
                        }
                    lab37: ;
                    }
                    bra = cursor;
                    if (find_among(a_9, null) == 0)
                    {
                        goto lab30;
                    }
                    ket = cursor;
                    if (current.Length < 5)
                    {
                        goto lab30;
                    }
                    B_is_verb = true;
                    B_is_noun = false;
                    slice_from("\u0627\u0633\u062A");
                }
            lab34: ;
            lab30: ;
                cursor = c15;
            }
            {
                int c20 = cursor;
                limit_backward = cursor;
                cursor = limit;
                ket = cursor;
                if (find_among_b(a_1, null) == 0)
                {
                    goto lab39;
                }
                bra = cursor;
                slice_from("\u0621");
                cursor = limit_backward;
            lab39: ;
                cursor = c20;
            }
            {
                int c21 = cursor;
                while (true)
                {
                    int c22 = cursor;
                    {
                        int c23 = cursor;
                        bra = cursor;
                        among_var = find_among(a_2, null);
                        if (among_var == 0)
                        {
                            goto lab43;
                        }
                        ket = cursor;
                        switch (among_var) {
                            case 1: {
                                slice_from("\u0627");
                                break;
                            }
                            case 2: {
                                slice_from("\u0648");
                                break;
                            }
                            case 3: {
                                slice_from("\u064A");
                                break;
                            }
                        }
                        goto lab42;
                    lab43: ;
                        cursor = c23;
                        if (cursor >= limit)
                        {
                            goto lab41;
                        }
                        cursor++;
                    }
                lab42: ;
                    continue;
                lab41: ;
                    cursor = c22;
                    break;
                }
                cursor = c21;
            }
            return true;
        }

    }
}

