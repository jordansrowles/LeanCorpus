// Generated from german.sbl by Snowball 3.2.0 - https://snowballstem.org/

#pragma warning disable 0164
#pragma warning disable 0162

namespace Snowball
{
    using System;
    using System.Text;

    ///<summary>
    ///  This class implements the stemming algorithm defined by a snowball script.
    ///  Generated from german.sbl by Snowball 3.2.0 - https://snowballstem.org/
    ///</summary>
    ///
    [System.CodeDom.Compiler.GeneratedCode("Snowball", "3.2.0")]
    public partial class GermanStemmer : Stemmer
    {
        private const string g_v = "aeiouyäöü";
        private const string g_et_ending = "Udfgklmnrstzä";
        private const string g_s_ending = "bdfghklmnrt";
        private const string g_st_ending = "bdfghklmnt";

        private static readonly Among[] a_0 = new[]
        {
            new Among("", -1, 5, 0),
            new Among("ae", 0, 2, 0),
            new Among("oe", 0, 3, 0),
            new Among("qu", 0, -1, 0),
            new Among("ue", 0, 4, 0),
            new Among("ß", 0, 1, 0)
        };

        private static readonly Among[] a_1 = new[]
        {
            new Among("", -1, 5, 0),
            new Among("U", 0, 2, 0),
            new Among("Y", 0, 1, 0),
            new Among("ä", 0, 3, 0),
            new Among("ö", 0, 4, 0),
            new Among("ü", 0, 2, 0)
        };

        private static readonly Among[] a_2 = new[]
        {
            new Among("e", -1, 3, 0),
            new Among("em", -1, 1, 0),
            new Among("en", -1, 3, 0),
            new Among("erinnen", 2, 2, 0),
            new Among("erin", -1, 2, 0),
            new Among("ln", -1, 5, 0),
            new Among("ern", -1, 2, 0),
            new Among("er", -1, 2, 0),
            new Among("s", -1, 4, 0),
            new Among("es", 8, 3, 0),
            new Among("lns", 8, 5, 0)
        };

        private static readonly Among[] a_3 = new[]
        {
            new Among("tick", -1, -1, 0),
            new Among("plan", -1, -1, 0),
            new Among("geordn", -1, -1, 0),
            new Among("intern", -1, -1, 0),
            new Among("tr", -1, -1, 0)
        };

        private static readonly Among[] a_4 = new[]
        {
            new Among("en", -1, 1, 0),
            new Among("er", -1, 1, 0),
            new Among("et", -1, 3, 0),
            new Among("st", -1, 2, 0),
            new Among("est", 3, 1, 0)
        };

        private static readonly Among[] a_5 = new[]
        {
            new Among("ig", -1, 1, 0),
            new Among("lich", -1, 1, 0)
        };

        private static readonly Among[] a_6 = new[]
        {
            new Among("end", -1, 1, 0),
            new Among("ig", -1, 2, 0),
            new Among("ung", -1, 1, 0),
            new Among("lich", -1, 3, 0),
            new Among("isch", -1, 2, 0),
            new Among("ik", -1, 2, 0),
            new Among("heit", -1, 3, 0),
            new Among("keit", -1, 4, 0)
        };

        private static readonly Among[] a_7 = new[]
        {
            new Among("'", -1, 1, 0),
            new Among("'sch", -1, 1, 0),
            new Among("'s", -1, 1, 0)
        };


        protected override bool stem()
        {
            int among_var;
            int I_x;
            int I_p2;
            int I_p1;
            {
                int c1 = cursor;
                {
                    int c2 = cursor;
                    while (true)
                    {
                        int c3 = cursor;
                        while (true)
                        {
                            int c4 = cursor;
                            if (in_grouping(g_v, 97, 252, false) != 0)
                            {
                                goto lab2;
                            }
                            bra = cursor;
                            {
                                int c5 = cursor;
                                if (!(eq_s("u")))
                                {
                                    goto lab4;
                                }
                                ket = cursor;
                                if (in_grouping(g_v, 97, 252, false) != 0)
                                {
                                    goto lab4;
                                }
                                slice_from("U");
                                goto lab3;
                            lab4: ;
                                cursor = c5;
                                if (!(eq_s("y")))
                                {
                                    goto lab2;
                                }
                                ket = cursor;
                                if (in_grouping(g_v, 97, 252, false) != 0)
                                {
                                    goto lab2;
                                }
                                slice_from("Y");
                            }
                        lab3: ;
                            cursor = c4;
                            break;
                        lab2: ;
                            cursor = c4;
                            if (cursor >= limit)
                            {
                                goto lab1;
                            }
                            cursor++;
                        }
                        continue;
                    lab1: ;
                        cursor = c3;
                        break;
                    }
                    cursor = c2;
                }
                while (true)
                {
                    int c6 = cursor;
                    bra = cursor;
                    among_var = find_among(a_0, null);
                    ket = cursor;
                    switch (among_var) {
                        case 1: {
                            slice_from("ss");
                            break;
                        }
                        case 2: {
                            slice_from("ä");
                            break;
                        }
                        case 3: {
                            slice_from("ö");
                            break;
                        }
                        case 4: {
                            slice_from("ü");
                            break;
                        }
                        case 5: {
                            if (cursor >= limit)
                            {
                                goto lab5;
                            }
                            cursor++;
                            break;
                        }
                    }
                    continue;
                lab5: ;
                    cursor = c6;
                    break;
                }
                cursor = c1;
            }
            {
                int c7 = cursor;
                I_p1 = limit;
                I_p2 = limit;
                {
                    int c8 = cursor;
                    {
                        int c = cursor + 3;
                        if (c > limit)
                        {
                            goto lab6;
                        }
                        cursor = c;
                    }
                    I_x = cursor;
                    cursor = c8;
                }
                {

                    int ret = out_grouping(g_v, 97, 252, true);
                    if (ret < 0)
                    {
                        goto lab6;
                    }

                    cursor += ret;
                }
                {

                    int ret = in_grouping(g_v, 97, 252, true);
                    if (ret < 0)
                    {
                        goto lab6;
                    }

                    cursor += ret;
                }
                I_p1 = cursor;
                if (I_p1 >= I_x)
                {
                    goto lab7;
                }
                I_p1 = I_x;
            lab7: ;
                {

                    int ret = out_grouping(g_v, 97, 252, true);
                    if (ret < 0)
                    {
                        goto lab6;
                    }

                    cursor += ret;
                }
                {

                    int ret = in_grouping(g_v, 97, 252, true);
                    if (ret < 0)
                    {
                        goto lab6;
                    }

                    cursor += ret;
                }
                I_p2 = cursor;
            lab6: ;
                cursor = c7;
            }
            limit_backward = cursor;
            cursor = limit;
            {
                int c9 = limit - cursor;
                ket = cursor;
                among_var = find_among_b(a_2, null);
                if (among_var == 0)
                {
                    goto lab9;
                }
                bra = cursor;
                if (I_p1 > cursor)
                {
                    goto lab9;
                }
                switch (among_var) {
                    case 1: {
                        if (!(eq_s_b("syst")))
                        {
                            goto lab10;
                        }
                        goto lab9;
                    lab10: ;
                        slice_del();
                        break;
                    }
                    case 2: {
                        slice_del();
                        break;
                    }
                    case 3: {
                        slice_del();
                        {
                            int c10 = limit - cursor;
                            ket = cursor;
                            if (!(eq_s_b("s")))
                            {
                                {
                                    cursor = limit - c10;
                                    goto lab11;
                                }
                            }
                            bra = cursor;
                            if (!(eq_s_b("nis")))
                            {
                                {
                                    cursor = limit - c10;
                                    goto lab11;
                                }
                            }
                            slice_del();
                        lab11: ;
                        }
                        break;
                    }
                    case 4: {
                        if (in_grouping_b(g_s_ending, 98, 116, false) != 0)
                        {
                            goto lab9;
                        }
                        slice_del();
                        break;
                    }
                    case 5: {
                        slice_from("l");
                        break;
                    }
                }
            lab9: ;
                cursor = limit - c9;
            }
            {
                int c11 = limit - cursor;
                ket = cursor;
                among_var = find_among_b(a_4, null);
                if (among_var == 0)
                {
                    goto lab12;
                }
                bra = cursor;
                if (I_p1 > cursor)
                {
                    goto lab12;
                }
                switch (among_var) {
                    case 1: {
                        slice_del();
                        break;
                    }
                    case 2: {
                        if (in_grouping_b(g_st_ending, 98, 116, false) != 0)
                        {
                            goto lab12;
                        }
                        {
                            int c = cursor - 3;
                            if (c < limit_backward)
                            {
                                goto lab12;
                            }
                            cursor = c;
                        }
                        slice_del();
                        break;
                    }
                    case 3: {
                        {
                            int c12 = limit - cursor;
                            if (in_grouping_b(g_et_ending, 85, 228, false) != 0)
                            {
                                goto lab12;
                            }
                            cursor = limit - c12;
                        }
                        {
                            int c13 = limit - cursor;
                            if (find_among_b(a_3, null) == 0)
                            {
                                goto lab13;
                            }
                            goto lab12;
                        lab13: ;
                            cursor = limit - c13;
                        }
                        slice_del();
                        break;
                    }
                }
            lab12: ;
                cursor = limit - c11;
            }
            {
                int c14 = limit - cursor;
                ket = cursor;
                among_var = find_among_b(a_6, null);
                if (among_var == 0)
                {
                    goto lab14;
                }
                bra = cursor;
                if (I_p2 > cursor)
                {
                    goto lab14;
                }
                switch (among_var) {
                    case 1: {
                        slice_del();
                        {
                            int c15 = limit - cursor;
                            ket = cursor;
                            if (!(eq_s_b("ig")))
                            {
                                {
                                    cursor = limit - c15;
                                    goto lab15;
                                }
                            }
                            bra = cursor;
                            if (!(eq_s_b("e")))
                            {
                                goto lab16;
                            }
                            {
                                cursor = limit - c15;
                                goto lab15;
                            }
                        lab16: ;
                            if (I_p2 > cursor)
                            {
                                {
                                    cursor = limit - c15;
                                    goto lab15;
                                }
                            }
                            slice_del();
                        lab15: ;
                        }
                        break;
                    }
                    case 2: {
                        if (!(eq_s_b("e")))
                        {
                            goto lab17;
                        }
                        goto lab14;
                    lab17: ;
                        slice_del();
                        break;
                    }
                    case 3: {
                        slice_del();
                        {
                            int c16 = limit - cursor;
                            ket = cursor;
                            if (!(eq_s_b("er")))
                            {
                                goto lab20;
                            }
                            goto lab19;
                        lab20: ;
                            if (!(eq_s_b("en")))
                            {
                                {
                                    cursor = limit - c16;
                                    goto lab18;
                                }
                            }
                        lab19: ;
                            bra = cursor;
                            if (I_p1 > cursor)
                            {
                                {
                                    cursor = limit - c16;
                                    goto lab18;
                                }
                            }
                            slice_del();
                        lab18: ;
                        }
                        break;
                    }
                    case 4: {
                        slice_del();
                        {
                            int c17 = limit - cursor;
                            ket = cursor;
                            if (find_among_b(a_5, null) == 0)
                            {
                                {
                                    cursor = limit - c17;
                                    goto lab21;
                                }
                            }
                            bra = cursor;
                            if (I_p2 > cursor)
                            {
                                {
                                    cursor = limit - c17;
                                    goto lab21;
                                }
                            }
                            slice_del();
                        lab21: ;
                        }
                        break;
                    }
                }
            lab14: ;
                cursor = limit - c14;
            }
            {
                int c18 = limit - cursor;
                ket = cursor;
                if (find_among_b(a_7, null) == 0)
                {
                    goto lab22;
                }
                bra = cursor;
                if (cursor <= limit_backward)
                {
                    goto lab22;
                }
                cursor--;
                if (cursor <= limit_backward)
                {
                    goto lab22;
                }
                slice_del();
            lab22: ;
                cursor = limit - c18;
            }
            cursor = limit_backward;
            {
                int c19 = cursor;
                while (true)
                {
                    int c20 = cursor;
                    bra = cursor;
                    among_var = find_among(a_1, null);
                    ket = cursor;
                    switch (among_var) {
                        case 1: {
                            slice_from("y");
                            break;
                        }
                        case 2: {
                            slice_from("u");
                            break;
                        }
                        case 3: {
                            slice_from("a");
                            break;
                        }
                        case 4: {
                            slice_from("o");
                            break;
                        }
                        case 5: {
                            if (cursor >= limit)
                            {
                                goto lab24;
                            }
                            cursor++;
                            break;
                        }
                    }
                    continue;
                lab24: ;
                    cursor = c20;
                    break;
                }
                cursor = c19;
            }
            return true;
        }

    }
}

