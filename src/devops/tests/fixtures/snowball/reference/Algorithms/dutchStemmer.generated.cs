// Generated from dutch.sbl by Snowball 3.2.0 - https://snowballstem.org/

#pragma warning disable 0164
#pragma warning disable 0162

namespace Snowball
{
    using System;
    using System.Text;

    ///<summary>
    ///  This class implements the stemming algorithm defined by a snowball script.
    ///  Generated from dutch.sbl by Snowball 3.2.0 - https://snowballstem.org/
    ///</summary>
    ///
    [System.CodeDom.Compiler.GeneratedCode("Snowball", "3.2.0")]
    public partial class DutchStemmer : Stemmer
    {
        private int I_p2;
        private int I_p1;
        private StringBuilder S_ch = new StringBuilder();

        private const string g_E = "eèéêë";
        private const string g_AIOU = "aiouàáâäìíîïòóôöùúûü";
        private const string g_AEIOU = "aeiouàáâäèéêëìíîïòóôöùúûü";
        private const string g_v = "aeiouyàáâäèéêëìíîïòóôöùúûü";
        private const string g_v_WX = "aeiouwxyàáâäèéêëìíîïòóôöùúûü";

        private static readonly Among[] a_0 = new[]
        {
            new Among("a", -1, 1, 0),
            new Among("e", -1, 2, 0),
            new Among("o", -1, 1, 0),
            new Among("u", -1, 1, 0),
            new Among("à", -1, 1, 0),
            new Among("á", -1, 1, 0),
            new Among("â", -1, 1, 0),
            new Among("ä", -1, 1, 0),
            new Among("è", -1, 2, 0),
            new Among("é", -1, 2, 0),
            new Among("ê", -1, 2, 0),
            new Among("eë", -1, 3, 0),
            new Among("ië", -1, 4, 0),
            new Among("ò", -1, 1, 0),
            new Among("ó", -1, 1, 0),
            new Among("ô", -1, 1, 0),
            new Among("ö", -1, 1, 0),
            new Among("ù", -1, 1, 0),
            new Among("ú", -1, 1, 0),
            new Among("û", -1, 1, 0),
            new Among("ü", -1, 1, 0)
        };

        private static readonly Among[] a_1 = new[]
        {
            new Among("nde", -1, 8, 0),
            new Among("en", -1, 7, 0),
            new Among("s", -1, 2, 0),
            new Among("'s", 2, 1, 0),
            new Among("es", 2, 4, 0),
            new Among("ies", 4, 3, 0),
            new Among("aus", 2, 6, 0),
            new Among("és", 2, 5, 0)
        };

        private static readonly Among[] a_2 = new[]
        {
            new Among("de", -1, 5, 0),
            new Among("ge", -1, 2, 0),
            new Among("ische", -1, 4, 0),
            new Among("je", -1, 1, 0),
            new Among("lijke", -1, 3, 0),
            new Among("le", -1, 9, 0),
            new Among("ene", -1, 10, 0),
            new Among("re", -1, 8, 0),
            new Among("se", -1, 7, 0),
            new Among("te", -1, 6, 0),
            new Among("ieve", -1, 11, 0)
        };

        private static readonly Among[] a_3 = new[]
        {
            new Among("heid", -1, 3, 0),
            new Among("fie", -1, 7, 0),
            new Among("gie", -1, 8, 0),
            new Among("atie", -1, 1, 0),
            new Among("isme", -1, 5, 0),
            new Among("ing", -1, 5, 0),
            new Among("arij", -1, 6, 0),
            new Among("erij", -1, 5, 0),
            new Among("sel", -1, 3, 0),
            new Among("rder", -1, 4, 0),
            new Among("ster", -1, 3, 0),
            new Among("iteit", -1, 2, 0),
            new Among("dst", -1, 10, 0),
            new Among("tst", -1, 9, 0)
        };

        private static readonly Among[] a_4 = new[]
        {
            new Among("end", -1, 9, 0),
            new Among("atief", -1, 2, 0),
            new Among("erig", -1, 9, 0),
            new Among("achtig", -1, 3, 0),
            new Among("ioneel", -1, 1, 0),
            new Among("baar", -1, 3, 0),
            new Among("laar", -1, 5, 0),
            new Among("naar", -1, 4, 0),
            new Among("raar", -1, 6, 0),
            new Among("eriger", -1, 9, 0),
            new Among("achtiger", -1, 3, 0),
            new Among("lijker", -1, 8, 0),
            new Among("tant", -1, 7, 0),
            new Among("erigst", -1, 9, 0),
            new Among("achtigst", -1, 3, 0),
            new Among("lijkst", -1, 8, 0)
        };

        private static readonly Among[] a_5 = new[]
        {
            new Among("ig", -1, 1, 0),
            new Among("iger", -1, 1, 0),
            new Among("igst", -1, 1, 0)
        };

        private static readonly Among[] a_6 = new[]
        {
            new Among("ft", -1, 2, 0),
            new Among("kt", -1, 1, 0),
            new Among("pt", -1, 3, 0)
        };

        private static readonly Among[] a_7 = new[]
        {
            new Among("bb", -1, 1, 0),
            new Among("cc", -1, 2, 0),
            new Among("dd", -1, 3, 0),
            new Among("ff", -1, 4, 0),
            new Among("gg", -1, 5, 0),
            new Among("hh", -1, 6, 0),
            new Among("jj", -1, 7, 0),
            new Among("kk", -1, 8, 0),
            new Among("ll", -1, 9, 0),
            new Among("mm", -1, 10, 0),
            new Among("nn", -1, 11, 0),
            new Among("pp", -1, 12, 0),
            new Among("qq", -1, 13, 0),
            new Among("rr", -1, 14, 0),
            new Among("ss", -1, 15, 0),
            new Among("tt", -1, 16, 0),
            new Among("v", -1, 4, 0),
            new Among("vv", 16, 17, 0),
            new Among("ww", -1, 18, 0),
            new Among("xx", -1, 19, 0),
            new Among("z", -1, 15, 0),
            new Among("zz", 20, 20, 0)
        };

        private static readonly Among[] a_8 = new[]
        {
            new Among("d", -1, 1, 0),
            new Among("t", -1, 2, 0)
        };

        private static readonly Among[] a_9 = new[]
        {
            new Among("", -1, -1, 0),
            new Among("eft", 0, 1, 0),
            new Among("vaa", 0, 1, 0),
            new Among("val", 0, 1, 0),
            new Among("vali", 3, -1, 0),
            new Among("vare", 0, 1, 0)
        };

        private static readonly Among[] a_10 = new[]
        {
            new Among("ë", -1, 1, 0),
            new Among("ï", -1, 2, 0)
        };

        private static readonly Among[] a_11 = new[]
        {
            new Among("ë", -1, 1, 0),
            new Among("ï", -1, 2, 0)
        };


        private bool r_V()
        {
            {
                int c1 = limit - cursor;
                if (in_grouping_b(g_v, 97, 252, false) != 0)
                {
                    goto lab1;
                }
                goto lab0;
            lab1: ;
                if (!(eq_s_b("ij")))
                {
                    return false;
                }
            lab0: ;
                cursor = limit - c1;
            }
            return true;
        }

        private bool r_C()
        {
            {
                int c1 = limit - cursor;
                if (!(eq_s_b("ij")))
                {
                    goto lab0;
                }
                return false;
            lab0: ;
                if (out_grouping_b(g_v, 97, 252, false) != 0)
                {
                    return false;
                }
                cursor = limit - c1;
            }
            return true;
        }

        private bool r_lengthen_V()
        {
            int among_var;
            {
                int c1 = limit - cursor;
                if (out_grouping_b(g_v_WX, 97, 252, false) != 0)
                {
                    goto lab0;
                }
                ket = cursor;
                among_var = find_among_b(a_0, null);
                if (among_var == 0)
                {
                    goto lab0;
                }
                bra = cursor;
                switch (among_var) {
                    case 1: {
                        {
                            int c2 = limit - cursor;
                            if (out_grouping_b(g_AEIOU, 97, 252, false) != 0)
                            {
                                goto lab2;
                            }
                            goto lab1;
                        lab2: ;
                            if (cursor > limit_backward)
                            {
                                goto lab0;
                            }
                        lab1: ;
                            cursor = limit - c2;
                        }
                        slice_to(S_ch);
                        {
                            int c = cursor;
                            insert(cursor, cursor, S_ch.ToString());
                            cursor = c;
                        }
                        break;
                    }
                    case 2: {
                        {
                            int c3 = limit - cursor;
                            if (out_grouping_b(g_AEIOU, 97, 252, false) != 0)
                            {
                                goto lab4;
                            }
                            goto lab3;
                        lab4: ;
                            if (cursor > limit_backward)
                            {
                                goto lab0;
                            }
                        lab3: ;
                            {
                                int c4 = limit - cursor;
                                if (in_grouping_b(g_AIOU, 97, 252, false) != 0)
                                {
                                    goto lab7;
                                }
                                goto lab6;
                            lab7: ;
                                if (in_grouping_b(g_E, 101, 235, false) != 0)
                                {
                                    goto lab5;
                                }
                                if (cursor > limit_backward)
                                {
                                    goto lab5;
                                }
                            lab6: ;
                                goto lab0;
                            lab5: ;
                                cursor = limit - c4;
                            }
                            {
                                int c5 = limit - cursor;
                                if (cursor <= limit_backward)
                                {
                                    goto lab8;
                                }
                                cursor--;
                                if (in_grouping_b(g_AIOU, 97, 252, false) != 0)
                                {
                                    goto lab8;
                                }
                                if (out_grouping_b(g_AEIOU, 97, 252, false) != 0)
                                {
                                    goto lab8;
                                }
                                goto lab0;
                            lab8: ;
                                cursor = limit - c5;
                            }
                            cursor = limit - c3;
                        }
                        slice_to(S_ch);
                        {
                            int c = cursor;
                            insert(cursor, cursor, S_ch.ToString());
                            cursor = c;
                        }
                        break;
                    }
                    case 3: {
                        slice_from("eëe");
                        break;
                    }
                    case 4: {
                        slice_from("iee");
                        break;
                    }
                }
            lab0: ;
                cursor = limit - c1;
            }
            return true;
        }

        private bool r_Step_1c()
        {
            int among_var;
            ket = cursor;
            among_var = find_among_b(a_8, null);
            if (among_var == 0)
            {
                return false;
            }
            bra = cursor;
            if (I_p1 > cursor)
            {
                return false;
            }
            if (!r_C())
                return false;
            switch (among_var) {
                case 1: {
                    {
                        int c1 = limit - cursor;
                        if (!(eq_s_b("n")))
                        {
                            goto lab0;
                        }
                        if (I_p1 > cursor)
                        {
                            goto lab0;
                        }
                        return false;
                    lab0: ;
                        cursor = limit - c1;
                    }
                    {
                        int c2 = limit - cursor;
                        if (!(eq_s_b("in")))
                        {
                            goto lab2;
                        }
                        if (cursor > limit_backward)
                        {
                            goto lab2;
                        }
                        slice_from("n");
                        goto lab1;
                    lab2: ;
                        cursor = limit - c2;
                        slice_del();
                    }
                lab1: ;
                    break;
                }
                case 2: {
                    {
                        int c3 = limit - cursor;
                        if (!(eq_s_b("h")))
                        {
                            goto lab3;
                        }
                        if (I_p1 > cursor)
                        {
                            goto lab3;
                        }
                        return false;
                    lab3: ;
                        cursor = limit - c3;
                    }
                    {
                        int c4 = limit - cursor;
                        if (!(eq_s_b("en")))
                        {
                            goto lab4;
                        }
                        if (cursor > limit_backward)
                        {
                            goto lab4;
                        }
                        return false;
                    lab4: ;
                        cursor = limit - c4;
                    }
                    slice_del();
                    break;
                }
            }
            return true;
        }

        private bool r_measure()
        {
            I_p1 = limit;
            I_p2 = limit;
            {
                int c1 = cursor;
                while (true)
                {
                    if (out_grouping(g_v, 97, 252, false) != 0)
                    {
                        goto lab1;
                    }
                    continue;
                lab1: ;
                    break;
                }
                {
                    int c2 = 1;
                    while (true)
                    {
                        int c3 = cursor;
                        if (!(eq_s("ij")))
                        {
                            goto lab4;
                        }
                        goto lab3;
                    lab4: ;
                        if (in_grouping(g_v, 97, 252, false) != 0)
                        {
                            goto lab2;
                        }
                    lab3: ;
                        c2--;
                        continue;
                    lab2: ;
                        cursor = c3;
                        break;
                    }
                    if (c2 > 0)
                    {
                        goto lab0;
                    }
                }
                if (out_grouping(g_v, 97, 252, false) != 0)
                {
                    goto lab0;
                }
                I_p1 = cursor;
                while (true)
                {
                    if (out_grouping(g_v, 97, 252, false) != 0)
                    {
                        goto lab5;
                    }
                    continue;
                lab5: ;
                    break;
                }
                {
                    int c4 = 1;
                    while (true)
                    {
                        int c5 = cursor;
                        if (!(eq_s("ij")))
                        {
                            goto lab8;
                        }
                        goto lab7;
                    lab8: ;
                        if (in_grouping(g_v, 97, 252, false) != 0)
                        {
                            goto lab6;
                        }
                    lab7: ;
                        c4--;
                        continue;
                    lab6: ;
                        cursor = c5;
                        break;
                    }
                    if (c4 > 0)
                    {
                        goto lab0;
                    }
                }
                if (out_grouping(g_v, 97, 252, false) != 0)
                {
                    goto lab0;
                }
                I_p2 = cursor;
            lab0: ;
                cursor = c1;
            }
            return true;
        }

        protected override bool stem()
        {
            int among_var;
            bool B_GE_removed;
            bool B_stemmed;
            B_stemmed = false;
            r_measure();
            limit_backward = cursor;
            cursor = limit;
            {
                int c1 = limit - cursor;
                ket = cursor;
                among_var = find_among_b(a_1, null);
                if (among_var == 0)
                {
                    goto lab0;
                }
                bra = cursor;
                switch (among_var) {
                    case 1: {
                        slice_del();
                        break;
                    }
                    case 2: {
                        if (I_p1 > cursor)
                        {
                            goto lab0;
                        }
                        {
                            int c2 = limit - cursor;
                            if (!(eq_s_b("t")))
                            {
                                goto lab1;
                            }
                            if (I_p1 > cursor)
                            {
                                goto lab1;
                            }
                            goto lab0;
                        lab1: ;
                            cursor = limit - c2;
                        }
                        if (!r_C())
                            goto lab0;
                        slice_del();
                        break;
                    }
                    case 3: {
                        if (I_p1 > cursor)
                        {
                            goto lab0;
                        }
                        slice_from("ie");
                        break;
                    }
                    case 4: {
                        {
                            int c3 = limit - cursor;
                            {
                                int c4 = limit - cursor;
                                if (!(eq_s_b("ar")))
                                {
                                    goto lab3;
                                }
                                if (I_p1 > cursor)
                                {
                                    goto lab3;
                                }
                                if (!r_C())
                                    goto lab3;
                                cursor = limit - c4;
                            }
                            slice_del();
                            r_lengthen_V();
                            goto lab2;
                        lab3: ;
                            cursor = limit - c3;
                            {
                                int c5 = limit - cursor;
                                if (!(eq_s_b("er")))
                                {
                                    goto lab4;
                                }
                                if (I_p1 > cursor)
                                {
                                    goto lab4;
                                }
                                if (!r_C())
                                    goto lab4;
                                cursor = limit - c5;
                            }
                            slice_del();
                            goto lab2;
                        lab4: ;
                            cursor = limit - c3;
                            if (I_p1 > cursor)
                            {
                                goto lab0;
                            }
                            if (!r_C())
                                goto lab0;
                            slice_from("e");
                        }
                    lab2: ;
                        break;
                    }
                    case 5: {
                        if (I_p1 > cursor)
                        {
                            goto lab0;
                        }
                        slice_from("é");
                        break;
                    }
                    case 6: {
                        if (I_p1 > cursor)
                        {
                            goto lab0;
                        }
                        if (!r_V())
                            goto lab0;
                        slice_from("au");
                        break;
                    }
                    case 7: {
                        {
                            int c6 = limit - cursor;
                            if (!(eq_s_b("hed")))
                            {
                                goto lab6;
                            }
                            if (I_p1 > cursor)
                            {
                                goto lab6;
                            }
                            bra = cursor;
                            slice_from("heid");
                            goto lab5;
                        lab6: ;
                            cursor = limit - c6;
                            if (!(eq_s_b("nd")))
                            {
                                goto lab7;
                            }
                            slice_del();
                            goto lab5;
                        lab7: ;
                            cursor = limit - c6;
                            if (!(eq_s_b("d")))
                            {
                                goto lab8;
                            }
                            if (I_p1 > cursor)
                            {
                                goto lab8;
                            }
                            if (!r_C())
                                goto lab8;
                            bra = cursor;
                            slice_del();
                            goto lab5;
                        lab8: ;
                            cursor = limit - c6;
                            if (!(eq_s_b("i")))
                            {
                                goto lab11;
                            }
                            goto lab10;
                        lab11: ;
                            if (!(eq_s_b("j")))
                            {
                                goto lab9;
                            }
                        lab10: ;
                            if (!r_V())
                                goto lab9;
                            slice_del();
                            goto lab5;
                        lab9: ;
                            cursor = limit - c6;
                            if (I_p1 > cursor)
                            {
                                goto lab0;
                            }
                            if (!r_C())
                                goto lab0;
                            slice_del();
                            r_lengthen_V();
                        }
                    lab5: ;
                        break;
                    }
                    case 8: {
                        slice_from("nd");
                        break;
                    }
                }
                B_stemmed = true;
            lab0: ;
                cursor = limit - c1;
            }
            {
                int c7 = limit - cursor;
                ket = cursor;
                among_var = find_among_b(a_2, null);
                if (among_var == 0)
                {
                    goto lab12;
                }
                bra = cursor;
                switch (among_var) {
                    case 1: {
                        {
                            int c8 = limit - cursor;
                            if (!(eq_s_b("'t")))
                            {
                                goto lab14;
                            }
                            bra = cursor;
                            slice_del();
                            goto lab13;
                        lab14: ;
                            cursor = limit - c8;
                            if (!(eq_s_b("et")))
                            {
                                goto lab15;
                            }
                            bra = cursor;
                            if (I_p1 > cursor)
                            {
                                goto lab15;
                            }
                            if (!r_C())
                                goto lab15;
                            slice_del();
                            goto lab13;
                        lab15: ;
                            cursor = limit - c8;
                            if (!(eq_s_b("rnt")))
                            {
                                goto lab16;
                            }
                            bra = cursor;
                            slice_from("rn");
                            goto lab13;
                        lab16: ;
                            cursor = limit - c8;
                            if (!(eq_s_b("t")))
                            {
                                goto lab17;
                            }
                            bra = cursor;
                            if (I_p1 > cursor)
                            {
                                goto lab17;
                            }
                            {
                                int c9 = limit - cursor;
                                if (cursor <= limit_backward)
                                {
                                    goto lab17;
                                }
                                cursor--;
                                if (in_grouping_b(g_v, 97, 252, false) != 0)
                                {
                                    goto lab19;
                                }
                                goto lab18;
                            lab19: ;
                                if (!(eq_s_b("ij")))
                                {
                                    goto lab17;
                                }
                            lab18: ;
                                cursor = limit - c9;
                            }
                            slice_del();
                            goto lab13;
                        lab17: ;
                            cursor = limit - c8;
                            if (!(eq_s_b("ink")))
                            {
                                goto lab20;
                            }
                            bra = cursor;
                            slice_from("ing");
                            goto lab13;
                        lab20: ;
                            cursor = limit - c8;
                            if (!(eq_s_b("mp")))
                            {
                                goto lab21;
                            }
                            bra = cursor;
                            slice_from("m");
                            goto lab13;
                        lab21: ;
                            cursor = limit - c8;
                            if (!(eq_s_b("'")))
                            {
                                goto lab22;
                            }
                            bra = cursor;
                            if (I_p1 > cursor)
                            {
                                goto lab22;
                            }
                            slice_del();
                            goto lab13;
                        lab22: ;
                            cursor = limit - c8;
                            bra = cursor;
                            if (I_p1 > cursor)
                            {
                                goto lab12;
                            }
                            if (!r_C())
                                goto lab12;
                            slice_del();
                        }
                    lab13: ;
                        break;
                    }
                    case 2: {
                        if (I_p1 > cursor)
                        {
                            goto lab12;
                        }
                        slice_from("g");
                        break;
                    }
                    case 3: {
                        if (I_p1 > cursor)
                        {
                            goto lab12;
                        }
                        slice_from("lijk");
                        break;
                    }
                    case 4: {
                        if (I_p1 > cursor)
                        {
                            goto lab12;
                        }
                        slice_from("isch");
                        break;
                    }
                    case 5: {
                        if (I_p1 > cursor)
                        {
                            goto lab12;
                        }
                        if (!r_C())
                            goto lab12;
                        slice_del();
                        break;
                    }
                    case 6: {
                        if (I_p1 > cursor)
                        {
                            goto lab12;
                        }
                        slice_from("t");
                        break;
                    }
                    case 7: {
                        if (I_p1 > cursor)
                        {
                            goto lab12;
                        }
                        slice_from("s");
                        break;
                    }
                    case 8: {
                        if (I_p1 > cursor)
                        {
                            goto lab12;
                        }
                        slice_from("r");
                        break;
                    }
                    case 9: {
                        if (I_p1 > cursor)
                        {
                            goto lab12;
                        }
                        slice_del();
                        insert(cursor, cursor, "l");
                        r_lengthen_V();
                        break;
                    }
                    case 10: {
                        if (I_p1 > cursor)
                        {
                            goto lab12;
                        }
                        if (!r_C())
                            goto lab12;
                        slice_del();
                        insert(cursor, cursor, "en");
                        r_lengthen_V();
                        break;
                    }
                    case 11: {
                        if (I_p1 > cursor)
                        {
                            goto lab12;
                        }
                        if (!r_C())
                            goto lab12;
                        slice_from("ief");
                        break;
                    }
                }
                B_stemmed = true;
            lab12: ;
                cursor = limit - c7;
            }
            {
                int c10 = limit - cursor;
                ket = cursor;
                among_var = find_among_b(a_3, null);
                if (among_var == 0)
                {
                    goto lab23;
                }
                bra = cursor;
                switch (among_var) {
                    case 1: {
                        if (I_p1 > cursor)
                        {
                            goto lab23;
                        }
                        slice_from("eer");
                        break;
                    }
                    case 2: {
                        if (I_p1 > cursor)
                        {
                            goto lab23;
                        }
                        slice_del();
                        r_lengthen_V();
                        break;
                    }
                    case 3: {
                        if (I_p1 > cursor)
                        {
                            goto lab23;
                        }
                        slice_del();
                        break;
                    }
                    case 4: {
                        slice_from("r");
                        break;
                    }
                    case 5: {
                        {
                            int c11 = limit - cursor;
                            if (!(eq_s_b("ild")))
                            {
                                goto lab25;
                            }
                            slice_from("er");
                            goto lab24;
                        lab25: ;
                            cursor = limit - c11;
                            if (I_p1 > cursor)
                            {
                                goto lab23;
                            }
                            slice_del();
                            r_lengthen_V();
                        }
                    lab24: ;
                        break;
                    }
                    case 6: {
                        if (I_p1 > cursor)
                        {
                            goto lab23;
                        }
                        if (!r_C())
                            goto lab23;
                        slice_from("aar");
                        break;
                    }
                    case 7: {
                        if (I_p2 > cursor)
                        {
                            goto lab23;
                        }
                        slice_del();
                        insert(cursor, cursor, "f");
                        r_lengthen_V();
                        break;
                    }
                    case 8: {
                        if (I_p2 > cursor)
                        {
                            goto lab23;
                        }
                        slice_del();
                        insert(cursor, cursor, "g");
                        r_lengthen_V();
                        break;
                    }
                    case 9: {
                        if (I_p1 > cursor)
                        {
                            goto lab23;
                        }
                        if (!r_C())
                            goto lab23;
                        slice_from("t");
                        break;
                    }
                    case 10: {
                        if (I_p1 > cursor)
                        {
                            goto lab23;
                        }
                        if (!r_C())
                            goto lab23;
                        slice_from("d");
                        break;
                    }
                }
                B_stemmed = true;
            lab23: ;
                cursor = limit - c10;
            }
            {
                int c12 = limit - cursor;
                {
                    int c13 = limit - cursor;
                    ket = cursor;
                    among_var = find_among_b(a_4, null);
                    if (among_var == 0)
                    {
                        goto lab28;
                    }
                    bra = cursor;
                    switch (among_var) {
                        case 1: {
                            if (I_p1 > cursor)
                            {
                                goto lab28;
                            }
                            slice_from("ie");
                            break;
                        }
                        case 2: {
                            if (I_p1 > cursor)
                            {
                                goto lab28;
                            }
                            slice_from("eer");
                            break;
                        }
                        case 3: {
                            if (I_p1 > cursor)
                            {
                                goto lab28;
                            }
                            slice_del();
                            break;
                        }
                        case 4: {
                            if (I_p1 > cursor)
                            {
                                goto lab28;
                            }
                            if (!r_V())
                                goto lab28;
                            slice_from("n");
                            break;
                        }
                        case 5: {
                            if (I_p1 > cursor)
                            {
                                goto lab28;
                            }
                            if (!r_V())
                                goto lab28;
                            slice_from("l");
                            break;
                        }
                        case 6: {
                            if (I_p1 > cursor)
                            {
                                goto lab28;
                            }
                            if (!r_V())
                                goto lab28;
                            slice_from("r");
                            break;
                        }
                        case 7: {
                            if (I_p1 > cursor)
                            {
                                goto lab28;
                            }
                            slice_from("teer");
                            break;
                        }
                        case 8: {
                            if (I_p1 > cursor)
                            {
                                goto lab28;
                            }
                            slice_from("lijk");
                            break;
                        }
                        case 9: {
                            if (I_p1 > cursor)
                            {
                                goto lab28;
                            }
                            if (!r_C())
                                goto lab28;
                            slice_del();
                            r_lengthen_V();
                            break;
                        }
                    }
                    goto lab27;
                lab28: ;
                    cursor = limit - c13;
                    ket = cursor;
                    if (find_among_b(a_5, null) == 0)
                    {
                        goto lab26;
                    }
                    bra = cursor;
                    if (I_p1 > cursor)
                    {
                        goto lab26;
                    }
                    {
                        int c14 = limit - cursor;
                        if (!(eq_s_b("inn")))
                        {
                            goto lab29;
                        }
                        if (cursor > limit_backward)
                        {
                            goto lab29;
                        }
                        goto lab26;
                    lab29: ;
                        cursor = limit - c14;
                    }
                    if (!r_C())
                        goto lab26;
                    slice_del();
                    r_lengthen_V();
                }
            lab27: ;
                B_stemmed = true;
            lab26: ;
                cursor = limit - c12;
            }
            cursor = limit_backward;
            B_GE_removed = false;
            {
                int c15 = cursor;
                int c16 = cursor;
                bra = cursor;
                if (!(eq_s("ge")))
                {
                    goto lab30;
                }
                ket = cursor;
                {
                    int c17 = cursor;
                    {
                        int c = cursor + 3;
                        if (c > limit)
                        {
                            goto lab30;
                        }
                        cursor = c;
                    }
                    cursor = c17;
                }
                {
                    int c18 = cursor;
                    while (true)
                    {
                        int c19 = cursor;
                        if (!(eq_s("ij")))
                        {
                            goto lab33;
                        }
                        goto lab32;
                    lab33: ;
                        if (in_grouping(g_v, 97, 252, false) != 0)
                        {
                            goto lab31;
                        }
                    lab32: ;
                        break;
                    lab31: ;
                        cursor = c19;
                        if (cursor >= limit)
                        {
                            goto lab30;
                        }
                        cursor++;
                    }
                    while (true)
                    {
                        int c20 = cursor;
                        if (!(eq_s("ij")))
                        {
                            goto lab36;
                        }
                        goto lab35;
                    lab36: ;
                        if (in_grouping(g_v, 97, 252, false) != 0)
                        {
                            goto lab34;
                        }
                    lab35: ;
                        continue;
                    lab34: ;
                        cursor = c20;
                        break;
                    }
                    if (cursor >= limit)
                    {
                        goto lab30;
                    }
                    cursor = c18;
                }
                among_var = find_among(a_9, null);
                switch (among_var) {
                    case 1: {
                        goto lab30;
                        break;
                    }
                }
                B_GE_removed = true;
                slice_del();
                {
                    int c21 = cursor;
                    bra = cursor;
                    among_var = find_among(a_10, null);
                    if (among_var == 0)
                    {
                        goto lab37;
                    }
                    ket = cursor;
                    switch (among_var) {
                        case 1: {
                            slice_from("e");
                            break;
                        }
                        case 2: {
                            slice_from("i");
                            break;
                        }
                    }
                lab37: ;
                    cursor = c21;
                }
                cursor = c16;
                r_measure();
            lab30: ;
                cursor = c15;
            }
            limit_backward = cursor;
            cursor = limit;
            {
                int c22 = limit - cursor;
                if (!B_GE_removed)
                {
                    goto lab38;
                }
                B_stemmed = true;
                if (!r_Step_1c())
                    goto lab38;
            lab38: ;
                cursor = limit - c22;
            }
            cursor = limit_backward;
            B_GE_removed = false;
            {
                int c23 = cursor;
                int c24 = cursor;
                if (cursor >= limit)
                {
                    goto lab39;
                }
                cursor++;
                while (true)
                {
                    bra = cursor;
                    if (!(eq_s("ge")))
                    {
                        goto lab40;
                    }
                    ket = cursor;
                    break;
                lab40: ;
                    if (cursor >= limit)
                    {
                        goto lab39;
                    }
                    cursor++;
                }
                {
                    int c25 = cursor;
                    {
                        int c = cursor + 3;
                        if (c > limit)
                        {
                            goto lab39;
                        }
                        cursor = c;
                    }
                    cursor = c25;
                }
                {
                    int c26 = cursor;
                    while (true)
                    {
                        int c27 = cursor;
                        if (!(eq_s("ij")))
                        {
                            goto lab43;
                        }
                        goto lab42;
                    lab43: ;
                        if (in_grouping(g_v, 97, 252, false) != 0)
                        {
                            goto lab41;
                        }
                    lab42: ;
                        break;
                    lab41: ;
                        cursor = c27;
                        if (cursor >= limit)
                        {
                            goto lab39;
                        }
                        cursor++;
                    }
                    while (true)
                    {
                        int c28 = cursor;
                        if (!(eq_s("ij")))
                        {
                            goto lab46;
                        }
                        goto lab45;
                    lab46: ;
                        if (in_grouping(g_v, 97, 252, false) != 0)
                        {
                            goto lab44;
                        }
                    lab45: ;
                        continue;
                    lab44: ;
                        cursor = c28;
                        break;
                    }
                    if (cursor >= limit)
                    {
                        goto lab39;
                    }
                    cursor = c26;
                }
                B_GE_removed = true;
                slice_del();
                {
                    int c29 = cursor;
                    bra = cursor;
                    among_var = find_among(a_11, null);
                    if (among_var == 0)
                    {
                        goto lab47;
                    }
                    ket = cursor;
                    switch (among_var) {
                        case 1: {
                            slice_from("e");
                            break;
                        }
                        case 2: {
                            slice_from("i");
                            break;
                        }
                    }
                lab47: ;
                    cursor = c29;
                }
                cursor = c24;
                r_measure();
            lab39: ;
                cursor = c23;
            }
            limit_backward = cursor;
            cursor = limit;
            {
                int c30 = limit - cursor;
                if (!B_GE_removed)
                {
                    goto lab48;
                }
                B_stemmed = true;
                if (!r_Step_1c())
                    goto lab48;
            lab48: ;
                cursor = limit - c30;
            }
            cursor = limit_backward;
            limit_backward = cursor;
            cursor = limit;
            {
                int c31 = limit - cursor;
                ket = cursor;
                among_var = find_among_b(a_6, null);
                if (among_var == 0)
                {
                    goto lab49;
                }
                bra = cursor;
                switch (among_var) {
                    case 1: {
                        slice_from("k");
                        break;
                    }
                    case 2: {
                        slice_from("f");
                        break;
                    }
                    case 3: {
                        slice_from("p");
                        break;
                    }
                }
                B_stemmed = true;
            lab49: ;
                cursor = limit - c31;
            }
            {
                int c32 = limit - cursor;
                if (!B_stemmed)
                {
                    goto lab50;
                }
                ket = cursor;
                among_var = find_among_b(a_7, null);
                if (among_var == 0)
                {
                    goto lab50;
                }
                bra = cursor;
                switch (among_var) {
                    case 1: {
                        slice_from("b");
                        break;
                    }
                    case 2: {
                        slice_from("c");
                        break;
                    }
                    case 3: {
                        slice_from("d");
                        break;
                    }
                    case 4: {
                        slice_from("f");
                        break;
                    }
                    case 5: {
                        slice_from("g");
                        break;
                    }
                    case 6: {
                        slice_from("h");
                        break;
                    }
                    case 7: {
                        slice_from("j");
                        break;
                    }
                    case 8: {
                        slice_from("k");
                        break;
                    }
                    case 9: {
                        slice_from("l");
                        break;
                    }
                    case 10: {
                        slice_from("m");
                        break;
                    }
                    case 11: {
                        {
                            int c33 = limit - cursor;
                            if (!(eq_s_b("i")))
                            {
                                goto lab51;
                            }
                            if (cursor > limit_backward)
                            {
                                goto lab51;
                            }
                            goto lab50;
                        lab51: ;
                            cursor = limit - c33;
                        }
                        slice_from("n");
                        break;
                    }
                    case 12: {
                        slice_from("p");
                        break;
                    }
                    case 13: {
                        slice_from("q");
                        break;
                    }
                    case 14: {
                        slice_from("r");
                        break;
                    }
                    case 15: {
                        slice_from("s");
                        break;
                    }
                    case 16: {
                        slice_from("t");
                        break;
                    }
                    case 17: {
                        slice_from("v");
                        break;
                    }
                    case 18: {
                        slice_from("w");
                        break;
                    }
                    case 19: {
                        slice_from("x");
                        break;
                    }
                    case 20: {
                        slice_from("z");
                        break;
                    }
                }
            lab50: ;
                cursor = limit - c32;
            }
            cursor = limit_backward;
            return true;
        }

    }
}

