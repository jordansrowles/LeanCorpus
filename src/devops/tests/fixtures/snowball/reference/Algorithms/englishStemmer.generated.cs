// Generated from english.sbl by Snowball 3.2.0 - https://snowballstem.org/

#pragma warning disable 0164
#pragma warning disable 0162

namespace Snowball
{
    using System;
    using System.Text;

    ///<summary>
    ///  This class implements the stemming algorithm defined by a snowball script.
    ///  Generated from english.sbl by Snowball 3.2.0 - https://snowballstem.org/
    ///</summary>
    ///
    [System.CodeDom.Compiler.GeneratedCode("Snowball", "3.2.0")]
    public partial class EnglishStemmer : Stemmer
    {
        private const string g_aeo = "aeo";
        private const string g_v = "aeiouy";
        private const string g_v_WXY = "Yaeiouwxy";
        private const string g_valid_LI = "cdeghkmnrt";

        private static readonly Among[] a_0 = new[]
        {
            new Among("arsen", -1, -1, 0),
            new Among("commun", -1, -1, 0),
            new Among("emerg", -1, -1, 0),
            new Among("gener", -1, -1, 0),
            new Among("inter", -1, -1, 0),
            new Among("later", -1, -1, 0),
            new Among("organ", -1, -1, 0),
            new Among("past", -1, -1, 0),
            new Among("univers", -1, -1, 0)
        };

        private static readonly Among[] a_1 = new[]
        {
            new Among("'", -1, 1, 0),
            new Among("'s'", 0, 1, 0),
            new Among("'s", -1, 1, 0)
        };

        private static readonly Among[] a_2 = new[]
        {
            new Among("ied", -1, 2, 0),
            new Among("s", -1, 3, 0),
            new Among("ies", 1, 2, 0),
            new Among("sses", 1, 1, 0),
            new Among("ss", 1, -1, 0),
            new Among("us", 1, -1, 0)
        };

        private static readonly Among[] a_3 = new[]
        {
            new Among("succ", -1, 1, 0),
            new Among("proc", -1, 1, 0),
            new Among("exc", -1, 1, 0)
        };

        private static readonly Among[] a_4 = new[]
        {
            new Among("even", -1, 2, 0),
            new Among("cann", -1, 2, 0),
            new Among("inn", -1, 2, 0),
            new Among("earr", -1, 2, 0),
            new Among("herr", -1, 2, 0),
            new Among("out", -1, 2, 0),
            new Among("y", -1, 1, 0)
        };

        private static readonly Among[] a_5 = new[]
        {
            new Among("", -1, -1, 0),
            new Among("ed", 0, 2, 0),
            new Among("eed", 1, 1, 0),
            new Among("ing", 0, 3, 0),
            new Among("edly", 0, 2, 0),
            new Among("eedly", 4, 1, 0),
            new Among("ingly", 0, 2, 0)
        };

        private static readonly Among[] a_6 = new[]
        {
            new Among("", -1, 3, 0),
            new Among("bb", 0, 2, 0),
            new Among("dd", 0, 2, 0),
            new Among("ff", 0, 2, 0),
            new Among("gg", 0, 2, 0),
            new Among("bl", 0, 1, 0),
            new Among("mm", 0, 2, 0),
            new Among("nn", 0, 2, 0),
            new Among("pp", 0, 2, 0),
            new Among("rr", 0, 2, 0),
            new Among("at", 0, 1, 0),
            new Among("tt", 0, 2, 0),
            new Among("iz", 0, 1, 0)
        };

        private static readonly Among[] a_7 = new[]
        {
            new Among("anci", -1, 3, 0),
            new Among("enci", -1, 2, 0),
            new Among("ogi", -1, 14, 0),
            new Among("li", -1, 16, 0),
            new Among("bli", 3, 12, 0),
            new Among("abli", 4, 4, 0),
            new Among("alli", 3, 8, 0),
            new Among("fulli", 3, 9, 0),
            new Among("lessli", 3, 15, 0),
            new Among("ousli", 3, 10, 0),
            new Among("entli", 3, 5, 0),
            new Among("aliti", -1, 8, 0),
            new Among("biliti", -1, 12, 0),
            new Among("iviti", -1, 11, 0),
            new Among("tional", -1, 1, 0),
            new Among("ational", 14, 7, 0),
            new Among("alism", -1, 8, 0),
            new Among("ation", -1, 7, 0),
            new Among("ization", 17, 6, 0),
            new Among("izer", -1, 6, 0),
            new Among("ator", -1, 7, 0),
            new Among("iveness", -1, 11, 0),
            new Among("fulness", -1, 9, 0),
            new Among("ousness", -1, 10, 0),
            new Among("ogist", -1, 13, 0)
        };

        private static readonly Among[] a_8 = new[]
        {
            new Among("icate", -1, 4, 0),
            new Among("ative", -1, 6, 0),
            new Among("alize", -1, 3, 0),
            new Among("iciti", -1, 4, 0),
            new Among("ical", -1, 4, 0),
            new Among("tional", -1, 1, 0),
            new Among("ational", 5, 2, 0),
            new Among("ful", -1, 5, 0),
            new Among("ness", -1, 5, 0)
        };

        private static readonly Among[] a_9 = new[]
        {
            new Among("ic", -1, 1, 0),
            new Among("ance", -1, 1, 0),
            new Among("ence", -1, 1, 0),
            new Among("able", -1, 1, 0),
            new Among("ible", -1, 1, 0),
            new Among("ate", -1, 1, 0),
            new Among("ive", -1, 1, 0),
            new Among("ize", -1, 1, 0),
            new Among("iti", -1, 1, 0),
            new Among("al", -1, 1, 0),
            new Among("ism", -1, 1, 0),
            new Among("ion", -1, 2, 0),
            new Among("er", -1, 1, 0),
            new Among("ous", -1, 1, 0),
            new Among("ant", -1, 1, 0),
            new Among("ent", -1, 1, 0),
            new Among("ment", 15, 1, 0),
            new Among("ement", 16, 1, 0)
        };

        private static readonly Among[] a_10 = new[]
        {
            new Among("e", -1, 1, 0),
            new Among("l", -1, 2, 0)
        };

        private static readonly Among[] a_11 = new[]
        {
            new Among("andes", -1, -1, 0),
            new Among("atlas", -1, -1, 0),
            new Among("bias", -1, -1, 0),
            new Among("cosmos", -1, -1, 0),
            new Among("early", -1, 6, 0),
            new Among("gently", -1, 4, 0),
            new Among("howe", -1, -1, 0),
            new Among("idly", -1, 3, 0),
            new Among("news", -1, -1, 0),
            new Among("only", -1, 7, 0),
            new Among("singly", -1, 8, 0),
            new Among("skies", -1, 2, 0),
            new Among("skis", -1, 1, 0),
            new Among("sky", -1, -1, 0),
            new Among("ugly", -1, 5, 0)
        };


        private bool r_shortv()
        {
            {
                int c1 = limit - cursor;
                if (out_grouping_b(g_v_WXY, 89, 121, false) != 0)
                {
                    goto lab1;
                }
                if (in_grouping_b(g_v, 97, 121, false) != 0)
                {
                    goto lab1;
                }
                if (out_grouping_b(g_v, 97, 121, false) != 0)
                {
                    goto lab1;
                }
                goto lab0;
            lab1: ;
                cursor = limit - c1;
                if (out_grouping_b(g_v, 97, 121, false) != 0)
                {
                    goto lab2;
                }
                if (in_grouping_b(g_v, 97, 121, false) != 0)
                {
                    goto lab2;
                }
                if (cursor > limit_backward)
                {
                    goto lab2;
                }
                goto lab0;
            lab2: ;
                cursor = limit - c1;
                if (!(eq_s_b("past")))
                {
                    return false;
                }
            }
        lab0: ;
            return true;
        }

        protected override bool stem()
        {
            int among_var;
            bool B_Y_found;
            int I_p2;
            int I_p1;
            {
                int c1 = cursor;
                bra = cursor;
                among_var = find_among(a_11, null);
                if (among_var == 0)
                {
                    goto lab1;
                }
                ket = cursor;
                if (cursor < limit)
                {
                    goto lab1;
                }
                switch (among_var) {
                    case 1: {
                        slice_from("ski");
                        break;
                    }
                    case 2: {
                        slice_from("sky");
                        break;
                    }
                    case 3: {
                        slice_from("idl");
                        break;
                    }
                    case 4: {
                        slice_from("gentl");
                        break;
                    }
                    case 5: {
                        slice_from("ugli");
                        break;
                    }
                    case 6: {
                        slice_from("earli");
                        break;
                    }
                    case 7: {
                        slice_from("onli");
                        break;
                    }
                    case 8: {
                        slice_from("singl");
                        break;
                    }
                }
                goto lab0;
            lab1: ;
                cursor = c1;
                {
                    int c = cursor + 3;
                    if (c > limit)
                    {
                        goto lab3;
                    }
                    cursor = c;
                }
                goto lab2;
            lab3: ;
                goto lab0;
            lab2: ;
                cursor = c1;
                B_Y_found = false;
                {
                    int c2 = cursor;
                    bra = cursor;
                    if (!(eq_s("'")))
                    {
                        goto lab5;
                    }
                    ket = cursor;
                    slice_del();
                lab5: ;
                    cursor = c2;
                }
                {
                    int c3 = cursor;
                    bra = cursor;
                    if (!(eq_s("y")))
                    {
                        goto lab6;
                    }
                    ket = cursor;
                    slice_from("Y");
                    B_Y_found = true;
                lab6: ;
                    cursor = c3;
                }
                {
                    int c4 = cursor;
                    while (true)
                    {
                        int c5 = cursor;
                        while (true)
                        {
                            int c6 = cursor;
                            if (in_grouping(g_v, 97, 121, false) != 0)
                            {
                                goto lab9;
                            }
                            bra = cursor;
                            if (!(eq_s("y")))
                            {
                                goto lab9;
                            }
                            ket = cursor;
                            cursor = c6;
                            break;
                        lab9: ;
                            cursor = c6;
                            if (cursor >= limit)
                            {
                                goto lab8;
                            }
                            cursor++;
                        }
                        slice_from("Y");
                        B_Y_found = true;
                        continue;
                    lab8: ;
                        cursor = c5;
                        break;
                    }
                    cursor = c4;
                }
                I_p1 = limit;
                I_p2 = limit;
                {
                    int c7 = cursor;
                    {
                        int c8 = cursor;
                        if (find_among(a_0, null) == 0)
                        {
                            goto lab13;
                        }
                        goto lab12;
                    lab13: ;
                        cursor = c8;
                        {

                            int ret = out_grouping(g_v, 97, 121, true);
                            if (ret < 0)
                            {
                                goto lab11;
                            }

                            cursor += ret;
                        }
                        {

                            int ret = in_grouping(g_v, 97, 121, true);
                            if (ret < 0)
                            {
                                goto lab11;
                            }

                            cursor += ret;
                        }
                    }
                lab12: ;
                    I_p1 = cursor;
                    {

                        int ret = out_grouping(g_v, 97, 121, true);
                        if (ret < 0)
                        {
                            goto lab11;
                        }

                        cursor += ret;
                    }
                    {

                        int ret = in_grouping(g_v, 97, 121, true);
                        if (ret < 0)
                        {
                            goto lab11;
                        }

                        cursor += ret;
                    }
                    I_p2 = cursor;
                lab11: ;
                    cursor = c7;
                }
                limit_backward = cursor;
                cursor = limit;
                {
                    int c9 = limit - cursor;
                    {
                        int c10 = limit - cursor;
                        ket = cursor;
                        if (find_among_b(a_1, null) == 0)
                        {
                            {
                                cursor = limit - c10;
                                goto lab15;
                            }
                        }
                        bra = cursor;
                        slice_del();
                    lab15: ;
                    }
                    ket = cursor;
                    among_var = find_among_b(a_2, null);
                    if (among_var == 0)
                    {
                        goto lab14;
                    }
                    bra = cursor;
                    switch (among_var) {
                        case 1: {
                            slice_from("ss");
                            break;
                        }
                        case 2: {
                            {
                                int c11 = limit - cursor;
                                {
                                    int c = cursor - 2;
                                    if (c < limit_backward)
                                    {
                                        goto lab17;
                                    }
                                    cursor = c;
                                }
                                slice_from("i");
                                goto lab16;
                            lab17: ;
                                cursor = limit - c11;
                                slice_from("ie");
                            }
                        lab16: ;
                            break;
                        }
                        case 3: {
                            if (cursor <= limit_backward)
                            {
                                goto lab14;
                            }
                            cursor--;
                            {

                                int ret = out_grouping_b(g_v, 97, 121, true);
                                if (ret < 0)
                                {
                                    goto lab14;
                                }

                                cursor -= ret;
                            }
                            slice_del();
                            break;
                        }
                    }
                lab14: ;
                    cursor = limit - c9;
                }
                {
                    int c12 = limit - cursor;
                    ket = cursor;
                    among_var = find_among_b(a_5, null);
                    bra = cursor;
                    {
                        int c13 = limit - cursor;
                        switch (among_var) {
                            case 1: {
                                {
                                    int c14 = limit - cursor;
                                    if (I_p1 > cursor)
                                    {
                                        goto lab21;
                                    }
                                    {
                                        int c15 = limit - cursor;
                                        if (find_among_b(a_3, null) == 0)
                                        {
                                            goto lab23;
                                        }
                                        if (cursor > limit_backward)
                                        {
                                            goto lab23;
                                        }
                                        goto lab22;
                                    lab23: ;
                                        cursor = limit - c15;
                                        slice_from("ee");
                                    }
                                lab22: ;
                                lab21: ;
                                    cursor = limit - c14;
                                }
                                break;
                            }
                            case 2: {
                                goto lab20;
                                break;
                            }
                            case 3: {
                                among_var = find_among_b(a_4, null);
                                if (among_var == 0)
                                {
                                    goto lab20;
                                }
                                switch (among_var) {
                                    case 1: {
                                        {
                                            int c16 = limit - cursor;
                                            if (out_grouping_b(g_v, 97, 121, false) != 0)
                                            {
                                                goto lab20;
                                            }
                                            if (cursor > limit_backward)
                                            {
                                                goto lab20;
                                            }
                                            cursor = limit - c16;
                                        }
                                        bra = cursor;
                                        slice_from("ie");
                                        break;
                                    }
                                    case 2: {
                                        if (cursor > limit_backward)
                                        {
                                            goto lab20;
                                        }
                                        break;
                                    }
                                }
                                break;
                            }
                        }
                        goto lab19;
                    lab20: ;
                        cursor = limit - c13;
                        {
                            int c17 = limit - cursor;
                            {

                                int ret = out_grouping_b(g_v, 97, 121, true);
                                if (ret < 0)
                                {
                                    goto lab18;
                                }

                                cursor -= ret;
                            }
                            cursor = limit - c17;
                        }
                        slice_del();
                        ket = cursor;
                        bra = cursor;
                        {
                            int c18 = limit - cursor;
                            among_var = find_among_b(a_6, null);
                            switch (among_var) {
                                case 1: {
                                    slice_from("e");
                                    goto lab18;
                                    break;
                                }
                                case 2: {
                                    {
                                        int c19 = limit - cursor;
                                        if (in_grouping_b(g_aeo, 97, 111, false) != 0)
                                        {
                                            goto lab24;
                                        }
                                        if (cursor > limit_backward)
                                        {
                                            goto lab24;
                                        }
                                        goto lab18;
                                    lab24: ;
                                        cursor = limit - c19;
                                    }
                                    break;
                                }
                                case 3: {
                                    if (cursor != I_p1)
                                    {
                                        goto lab18;
                                    }
                                    {
                                        int c20 = limit - cursor;
                                        if (!r_shortv())
                                            goto lab18;
                                        cursor = limit - c20;
                                    }
                                    slice_from("e");
                                    goto lab18;
                                    break;
                                }
                            }
                            cursor = limit - c18;
                        }
                        ket = cursor;
                        if (cursor <= limit_backward)
                        {
                            goto lab18;
                        }
                        cursor--;
                        bra = cursor;
                        slice_del();
                    }
                lab19: ;
                lab18: ;
                    cursor = limit - c12;
                }
                {
                    int c21 = limit - cursor;
                    ket = cursor;
                    if (!(eq_s_b("y")))
                    {
                        goto lab27;
                    }
                    goto lab26;
                lab27: ;
                    if (!(eq_s_b("Y")))
                    {
                        goto lab25;
                    }
                lab26: ;
                    bra = cursor;
                    if (out_grouping_b(g_v, 97, 121, false) != 0)
                    {
                        goto lab25;
                    }
                    if (cursor <= limit_backward)
                    {
                        goto lab25;
                    }
                    slice_from("i");
                lab25: ;
                    cursor = limit - c21;
                }
                {
                    int c22 = limit - cursor;
                    ket = cursor;
                    among_var = find_among_b(a_7, null);
                    if (among_var == 0)
                    {
                        goto lab28;
                    }
                    bra = cursor;
                    if (I_p1 > cursor)
                    {
                        goto lab28;
                    }
                    switch (among_var) {
                        case 1: {
                            slice_from("tion");
                            break;
                        }
                        case 2: {
                            slice_from("ence");
                            break;
                        }
                        case 3: {
                            slice_from("ance");
                            break;
                        }
                        case 4: {
                            slice_from("able");
                            break;
                        }
                        case 5: {
                            slice_from("ent");
                            break;
                        }
                        case 6: {
                            slice_from("ize");
                            break;
                        }
                        case 7: {
                            slice_from("ate");
                            break;
                        }
                        case 8: {
                            slice_from("al");
                            break;
                        }
                        case 9: {
                            slice_from("ful");
                            break;
                        }
                        case 10: {
                            slice_from("ous");
                            break;
                        }
                        case 11: {
                            slice_from("ive");
                            break;
                        }
                        case 12: {
                            slice_from("ble");
                            break;
                        }
                        case 13: {
                            slice_from("og");
                            break;
                        }
                        case 14: {
                            if (!(eq_s_b("l")))
                            {
                                goto lab28;
                            }
                            slice_from("og");
                            break;
                        }
                        case 15: {
                            slice_from("less");
                            break;
                        }
                        case 16: {
                            if (in_grouping_b(g_valid_LI, 99, 116, false) != 0)
                            {
                                goto lab28;
                            }
                            slice_del();
                            break;
                        }
                    }
                lab28: ;
                    cursor = limit - c22;
                }
                {
                    int c23 = limit - cursor;
                    ket = cursor;
                    among_var = find_among_b(a_8, null);
                    if (among_var == 0)
                    {
                        goto lab29;
                    }
                    bra = cursor;
                    if (I_p1 > cursor)
                    {
                        goto lab29;
                    }
                    switch (among_var) {
                        case 1: {
                            slice_from("tion");
                            break;
                        }
                        case 2: {
                            slice_from("ate");
                            break;
                        }
                        case 3: {
                            slice_from("al");
                            break;
                        }
                        case 4: {
                            slice_from("ic");
                            break;
                        }
                        case 5: {
                            slice_del();
                            break;
                        }
                        case 6: {
                            if (I_p2 > cursor)
                            {
                                goto lab29;
                            }
                            slice_del();
                            break;
                        }
                    }
                lab29: ;
                    cursor = limit - c23;
                }
                {
                    int c24 = limit - cursor;
                    ket = cursor;
                    among_var = find_among_b(a_9, null);
                    if (among_var == 0)
                    {
                        goto lab30;
                    }
                    bra = cursor;
                    if (I_p2 > cursor)
                    {
                        goto lab30;
                    }
                    switch (among_var) {
                        case 1: {
                            slice_del();
                            break;
                        }
                        case 2: {
                            if (!(eq_s_b("s")))
                            {
                                goto lab32;
                            }
                            goto lab31;
                        lab32: ;
                            if (!(eq_s_b("t")))
                            {
                                goto lab30;
                            }
                        lab31: ;
                            slice_del();
                            break;
                        }
                    }
                lab30: ;
                    cursor = limit - c24;
                }
                {
                    int c25 = limit - cursor;
                    ket = cursor;
                    among_var = find_among_b(a_10, null);
                    if (among_var == 0)
                    {
                        goto lab33;
                    }
                    bra = cursor;
                    switch (among_var) {
                        case 1: {
                            if (I_p2 > cursor)
                            {
                                goto lab35;
                            }
                            goto lab34;
                        lab35: ;
                            if (I_p1 > cursor)
                            {
                                goto lab33;
                            }
                            {
                                int c26 = limit - cursor;
                                if (!r_shortv())
                                    goto lab36;
                                goto lab33;
                            lab36: ;
                                cursor = limit - c26;
                            }
                        lab34: ;
                            slice_del();
                            break;
                        }
                        case 2: {
                            if (I_p2 > cursor)
                            {
                                goto lab33;
                            }
                            if (!(eq_s_b("l")))
                            {
                                goto lab33;
                            }
                            slice_del();
                            break;
                        }
                    }
                lab33: ;
                    cursor = limit - c25;
                }
                cursor = limit_backward;
                {
                    int c27 = cursor;
                    if (!B_Y_found)
                    {
                        goto lab37;
                    }
                    while (true)
                    {
                        int c28 = cursor;
                        while (true)
                        {
                            int c29 = cursor;
                            bra = cursor;
                            if (!(eq_s("Y")))
                            {
                                goto lab39;
                            }
                            ket = cursor;
                            cursor = c29;
                            break;
                        lab39: ;
                            cursor = c29;
                            if (cursor >= limit)
                            {
                                goto lab38;
                            }
                            cursor++;
                        }
                        slice_from("y");
                        continue;
                    lab38: ;
                        cursor = c28;
                        break;
                    }
                lab37: ;
                    cursor = c27;
                }
            }
        lab0: ;
            return true;
        }

    }
}

