// Generated from portuguese.sbl by Snowball 3.2.0 - https://snowballstem.org/

#pragma warning disable 0164
#pragma warning disable 0162

namespace Snowball
{
    using System;
    using System.Text;

    ///<summary>
    ///  This class implements the stemming algorithm defined by a snowball script.
    ///  Generated from portuguese.sbl by Snowball 3.2.0 - https://snowballstem.org/
    ///</summary>
    ///
    [System.CodeDom.Compiler.GeneratedCode("Snowball", "3.2.0")]
    public partial class PortugueseStemmer : Stemmer
    {
        private const string g_v = "aeiouáâéêíóôú";

        private static readonly Among[] a_0 = new[]
        {
            new Among("", -1, 3, 0),
            new Among("ã", 0, 1, 0),
            new Among("õ", 0, 2, 0)
        };

        private static readonly Among[] a_1 = new[]
        {
            new Among("", -1, 3, 0),
            new Among("a~", 0, 1, 0),
            new Among("o~", 0, 2, 0)
        };

        private static readonly Among[] a_2 = new[]
        {
            new Among("ic", -1, -1, 0),
            new Among("ad", -1, -1, 0),
            new Among("os", -1, -1, 0),
            new Among("iv", -1, 1, 0)
        };

        private static readonly Among[] a_3 = new[]
        {
            new Among("ante", -1, 1, 0),
            new Among("avel", -1, 1, 0),
            new Among("ível", -1, 1, 0)
        };

        private static readonly Among[] a_4 = new[]
        {
            new Among("ic", -1, 1, 0),
            new Among("abil", -1, 1, 0),
            new Among("iv", -1, 1, 0)
        };

        private static readonly Among[] a_5 = new[]
        {
            new Among("ica", -1, 1, 0),
            new Among("ância", -1, 1, 0),
            new Among("ência", -1, 4, 0),
            new Among("logia", -1, 2, 0),
            new Among("ira", -1, 9, 0),
            new Among("adora", -1, 1, 0),
            new Among("osa", -1, 1, 0),
            new Among("ista", -1, 1, 0),
            new Among("iva", -1, 8, 0),
            new Among("eza", -1, 1, 0),
            new Among("idade", -1, 7, 0),
            new Among("ante", -1, 1, 0),
            new Among("mente", -1, 6, 0),
            new Among("amente", 12, 5, 0),
            new Among("ável", -1, 1, 0),
            new Among("ível", -1, 1, 0),
            new Among("ico", -1, 1, 0),
            new Among("ismo", -1, 1, 0),
            new Among("oso", -1, 1, 0),
            new Among("amento", -1, 1, 0),
            new Among("imento", -1, 1, 0),
            new Among("ivo", -1, 8, 0),
            new Among("aça~o", -1, 1, 0),
            new Among("uça~o", -1, 3, 0),
            new Among("ador", -1, 1, 0),
            new Among("icas", -1, 1, 0),
            new Among("ências", -1, 4, 0),
            new Among("logias", -1, 2, 0),
            new Among("iras", -1, 9, 0),
            new Among("adoras", -1, 1, 0),
            new Among("osas", -1, 1, 0),
            new Among("istas", -1, 1, 0),
            new Among("ivas", -1, 8, 0),
            new Among("ezas", -1, 1, 0),
            new Among("idades", -1, 7, 0),
            new Among("adores", -1, 1, 0),
            new Among("antes", -1, 1, 0),
            new Among("aço~es", -1, 1, 0),
            new Among("uço~es", -1, 3, 0),
            new Among("icos", -1, 1, 0),
            new Among("ismos", -1, 1, 0),
            new Among("osos", -1, 1, 0),
            new Among("amentos", -1, 1, 0),
            new Among("imentos", -1, 1, 0),
            new Among("ivos", -1, 8, 0)
        };

        private static readonly Among[] a_6 = new[]
        {
            new Among("ada", -1, 1, 0),
            new Among("ida", -1, 1, 0),
            new Among("ia", -1, 1, 0),
            new Among("aria", 2, 1, 0),
            new Among("eria", 2, 1, 0),
            new Among("iria", 2, 1, 0),
            new Among("ara", -1, 1, 0),
            new Among("era", -1, 1, 0),
            new Among("ira", -1, 1, 0),
            new Among("ava", -1, 1, 0),
            new Among("asse", -1, 1, 0),
            new Among("esse", -1, 1, 0),
            new Among("isse", -1, 1, 0),
            new Among("aste", -1, 1, 0),
            new Among("este", -1, 1, 0),
            new Among("iste", -1, 1, 0),
            new Among("ei", -1, 1, 0),
            new Among("arei", 16, 1, 0),
            new Among("erei", 16, 1, 0),
            new Among("irei", 16, 1, 0),
            new Among("am", -1, 1, 0),
            new Among("iam", 20, 1, 0),
            new Among("ariam", 21, 1, 0),
            new Among("eriam", 21, 1, 0),
            new Among("iriam", 21, 1, 0),
            new Among("aram", 20, 1, 0),
            new Among("eram", 20, 1, 0),
            new Among("iram", 20, 1, 0),
            new Among("avam", 20, 1, 0),
            new Among("em", -1, 1, 0),
            new Among("arem", 29, 1, 0),
            new Among("erem", 29, 1, 0),
            new Among("irem", 29, 1, 0),
            new Among("assem", 29, 1, 0),
            new Among("essem", 29, 1, 0),
            new Among("issem", 29, 1, 0),
            new Among("ado", -1, 1, 0),
            new Among("ido", -1, 1, 0),
            new Among("ando", -1, 1, 0),
            new Among("endo", -1, 1, 0),
            new Among("indo", -1, 1, 0),
            new Among("ara~o", -1, 1, 0),
            new Among("era~o", -1, 1, 0),
            new Among("ira~o", -1, 1, 0),
            new Among("ar", -1, 1, 0),
            new Among("er", -1, 1, 0),
            new Among("ir", -1, 1, 0),
            new Among("as", -1, 1, 0),
            new Among("adas", 47, 1, 0),
            new Among("idas", 47, 1, 0),
            new Among("ias", 47, 1, 0),
            new Among("arias", 50, 1, 0),
            new Among("erias", 50, 1, 0),
            new Among("irias", 50, 1, 0),
            new Among("aras", 47, 1, 0),
            new Among("eras", 47, 1, 0),
            new Among("iras", 47, 1, 0),
            new Among("avas", 47, 1, 0),
            new Among("es", -1, 1, 0),
            new Among("ardes", 58, 1, 0),
            new Among("erdes", 58, 1, 0),
            new Among("irdes", 58, 1, 0),
            new Among("ares", 58, 1, 0),
            new Among("eres", 58, 1, 0),
            new Among("ires", 58, 1, 0),
            new Among("asses", 58, 1, 0),
            new Among("esses", 58, 1, 0),
            new Among("isses", 58, 1, 0),
            new Among("astes", 58, 1, 0),
            new Among("estes", 58, 1, 0),
            new Among("istes", 58, 1, 0),
            new Among("is", -1, 1, 0),
            new Among("ais", 71, 1, 0),
            new Among("eis", 71, 1, 0),
            new Among("areis", 73, 1, 0),
            new Among("ereis", 73, 1, 0),
            new Among("ireis", 73, 1, 0),
            new Among("áreis", 73, 1, 0),
            new Among("éreis", 73, 1, 0),
            new Among("íreis", 73, 1, 0),
            new Among("ásseis", 73, 1, 0),
            new Among("ésseis", 73, 1, 0),
            new Among("ísseis", 73, 1, 0),
            new Among("áveis", 73, 1, 0),
            new Among("íeis", 73, 1, 0),
            new Among("aríeis", 84, 1, 0),
            new Among("eríeis", 84, 1, 0),
            new Among("iríeis", 84, 1, 0),
            new Among("ados", -1, 1, 0),
            new Among("idos", -1, 1, 0),
            new Among("amos", -1, 1, 0),
            new Among("áramos", 90, 1, 0),
            new Among("éramos", 90, 1, 0),
            new Among("íramos", 90, 1, 0),
            new Among("ávamos", 90, 1, 0),
            new Among("íamos", 90, 1, 0),
            new Among("aríamos", 95, 1, 0),
            new Among("eríamos", 95, 1, 0),
            new Among("iríamos", 95, 1, 0),
            new Among("emos", -1, 1, 0),
            new Among("aremos", 99, 1, 0),
            new Among("eremos", 99, 1, 0),
            new Among("iremos", 99, 1, 0),
            new Among("ássemos", 99, 1, 0),
            new Among("êssemos", 99, 1, 0),
            new Among("íssemos", 99, 1, 0),
            new Among("imos", -1, 1, 0),
            new Among("armos", -1, 1, 0),
            new Among("ermos", -1, 1, 0),
            new Among("irmos", -1, 1, 0),
            new Among("ámos", -1, 1, 0),
            new Among("arás", -1, 1, 0),
            new Among("erás", -1, 1, 0),
            new Among("irás", -1, 1, 0),
            new Among("eu", -1, 1, 0),
            new Among("iu", -1, 1, 0),
            new Among("ou", -1, 1, 0),
            new Among("ará", -1, 1, 0),
            new Among("erá", -1, 1, 0),
            new Among("irá", -1, 1, 0)
        };

        private static readonly Among[] a_7 = new[]
        {
            new Among("a", -1, 1, 0),
            new Among("i", -1, 1, 0),
            new Among("o", -1, 1, 0),
            new Among("os", -1, 1, 0),
            new Among("á", -1, 1, 0),
            new Among("í", -1, 1, 0),
            new Among("ó", -1, 1, 0)
        };

        private static readonly Among[] a_8 = new[]
        {
            new Among("e", -1, 1, 0),
            new Among("ç", -1, 2, 0),
            new Among("é", -1, 1, 0),
            new Among("ê", -1, 1, 0)
        };


        protected override bool stem()
        {
            int among_var;
            int I_p2;
            int I_p1;
            int I_pV;
            {
                int c1 = cursor;
                while (true)
                {
                    int c2 = cursor;
                    bra = cursor;
                    among_var = find_among(a_0, null);
                    ket = cursor;
                    switch (among_var) {
                        case 1: {
                            slice_from("a~");
                            break;
                        }
                        case 2: {
                            slice_from("o~");
                            break;
                        }
                        case 3: {
                            if (cursor >= limit)
                            {
                                goto lab1;
                            }
                            cursor++;
                            break;
                        }
                    }
                    continue;
                lab1: ;
                    cursor = c2;
                    break;
                }
                cursor = c1;
            }
            I_pV = limit;
            I_p1 = limit;
            I_p2 = limit;
            {
                int c3 = cursor;
                {
                    int c4 = cursor;
                    if (in_grouping(g_v, 97, 250, false) != 0)
                    {
                        goto lab5;
                    }
                    {
                        int c5 = cursor;
                        if (out_grouping(g_v, 97, 250, false) != 0)
                        {
                            goto lab7;
                        }
                        {

                            int ret = out_grouping(g_v, 97, 250, true);
                            if (ret < 0)
                            {
                                goto lab7;
                            }

                            cursor += ret;
                        }
                        goto lab6;
                    lab7: ;
                        cursor = c5;
                        if (in_grouping(g_v, 97, 250, false) != 0)
                        {
                            goto lab5;
                        }
                        {

                            int ret = in_grouping(g_v, 97, 250, true);
                            if (ret < 0)
                            {
                                goto lab5;
                            }

                            cursor += ret;
                        }
                    }
                lab6: ;
                    goto lab4;
                lab5: ;
                    cursor = c4;
                    if (out_grouping(g_v, 97, 250, false) != 0)
                    {
                        goto lab3;
                    }
                    {
                        int c6 = cursor;
                        if (out_grouping(g_v, 97, 250, false) != 0)
                        {
                            goto lab9;
                        }
                        {

                            int ret = out_grouping(g_v, 97, 250, true);
                            if (ret < 0)
                            {
                                goto lab9;
                            }

                            cursor += ret;
                        }
                        goto lab8;
                    lab9: ;
                        cursor = c6;
                        if (in_grouping(g_v, 97, 250, false) != 0)
                        {
                            goto lab3;
                        }
                        if (cursor >= limit)
                        {
                            goto lab3;
                        }
                        cursor++;
                    }
                lab8: ;
                }
            lab4: ;
                I_pV = cursor;
            lab3: ;
                cursor = c3;
            }
            {
                int c7 = cursor;
                {

                    int ret = out_grouping(g_v, 97, 250, true);
                    if (ret < 0)
                    {
                        goto lab10;
                    }

                    cursor += ret;
                }
                {

                    int ret = in_grouping(g_v, 97, 250, true);
                    if (ret < 0)
                    {
                        goto lab10;
                    }

                    cursor += ret;
                }
                I_p1 = cursor;
                {

                    int ret = out_grouping(g_v, 97, 250, true);
                    if (ret < 0)
                    {
                        goto lab10;
                    }

                    cursor += ret;
                }
                {

                    int ret = in_grouping(g_v, 97, 250, true);
                    if (ret < 0)
                    {
                        goto lab10;
                    }

                    cursor += ret;
                }
                I_p2 = cursor;
            lab10: ;
                cursor = c7;
            }
            limit_backward = cursor;
            cursor = limit;
            {
                int c8 = limit - cursor;
                {
                    int c9 = limit - cursor;
                    int c10 = limit - cursor;
                    {
                        int c11 = limit - cursor;
                        ket = cursor;
                        among_var = find_among_b(a_5, null);
                        if (among_var == 0)
                        {
                            goto lab15;
                        }
                        bra = cursor;
                        switch (among_var) {
                            case 1: {
                                if (I_p2 > cursor)
                                {
                                    goto lab15;
                                }
                                slice_del();
                                break;
                            }
                            case 2: {
                                if (I_p2 > cursor)
                                {
                                    goto lab15;
                                }
                                slice_from("log");
                                break;
                            }
                            case 3: {
                                if (I_p2 > cursor)
                                {
                                    goto lab15;
                                }
                                slice_from("u");
                                break;
                            }
                            case 4: {
                                if (I_p2 > cursor)
                                {
                                    goto lab15;
                                }
                                slice_from("ente");
                                break;
                            }
                            case 5: {
                                if (I_p1 > cursor)
                                {
                                    goto lab15;
                                }
                                slice_del();
                                {
                                    int c12 = limit - cursor;
                                    ket = cursor;
                                    among_var = find_among_b(a_2, null);
                                    if (among_var == 0)
                                    {
                                        {
                                            cursor = limit - c12;
                                            goto lab16;
                                        }
                                    }
                                    bra = cursor;
                                    if (I_p2 > cursor)
                                    {
                                        {
                                            cursor = limit - c12;
                                            goto lab16;
                                        }
                                    }
                                    slice_del();
                                    switch (among_var) {
                                        case 1: {
                                            ket = cursor;
                                            if (!(eq_s_b("at")))
                                            {
                                                {
                                                    cursor = limit - c12;
                                                    goto lab16;
                                                }
                                            }
                                            bra = cursor;
                                            if (I_p2 > cursor)
                                            {
                                                {
                                                    cursor = limit - c12;
                                                    goto lab16;
                                                }
                                            }
                                            slice_del();
                                            break;
                                        }
                                    }
                                lab16: ;
                                }
                                break;
                            }
                            case 6: {
                                if (I_p2 > cursor)
                                {
                                    goto lab15;
                                }
                                slice_del();
                                {
                                    int c13 = limit - cursor;
                                    ket = cursor;
                                    if (find_among_b(a_3, null) == 0)
                                    {
                                        {
                                            cursor = limit - c13;
                                            goto lab17;
                                        }
                                    }
                                    bra = cursor;
                                    if (I_p2 > cursor)
                                    {
                                        {
                                            cursor = limit - c13;
                                            goto lab17;
                                        }
                                    }
                                    slice_del();
                                lab17: ;
                                }
                                break;
                            }
                            case 7: {
                                if (I_p2 > cursor)
                                {
                                    goto lab15;
                                }
                                slice_del();
                                {
                                    int c14 = limit - cursor;
                                    ket = cursor;
                                    if (find_among_b(a_4, null) == 0)
                                    {
                                        {
                                            cursor = limit - c14;
                                            goto lab18;
                                        }
                                    }
                                    bra = cursor;
                                    if (I_p2 > cursor)
                                    {
                                        {
                                            cursor = limit - c14;
                                            goto lab18;
                                        }
                                    }
                                    slice_del();
                                lab18: ;
                                }
                                break;
                            }
                            case 8: {
                                if (I_p2 > cursor)
                                {
                                    goto lab15;
                                }
                                slice_del();
                                {
                                    int c15 = limit - cursor;
                                    ket = cursor;
                                    if (!(eq_s_b("at")))
                                    {
                                        {
                                            cursor = limit - c15;
                                            goto lab19;
                                        }
                                    }
                                    bra = cursor;
                                    if (I_p2 > cursor)
                                    {
                                        {
                                            cursor = limit - c15;
                                            goto lab19;
                                        }
                                    }
                                    slice_del();
                                lab19: ;
                                }
                                break;
                            }
                            case 9: {
                                if (I_pV > cursor)
                                {
                                    goto lab15;
                                }
                                if (!(eq_s_b("e")))
                                {
                                    goto lab15;
                                }
                                slice_from("ir");
                                break;
                            }
                        }
                        goto lab14;
                    lab15: ;
                        cursor = limit - c11;
                        if (cursor < I_pV)
                        {
                            goto lab13;
                        }
                        int c16 = limit_backward;
                        limit_backward = I_pV;
                        ket = cursor;
                        if (find_among_b(a_6, null) == 0)
                        {
                            {
                                limit_backward = c16;
                                goto lab13;
                            }
                        }
                        bra = cursor;
                        slice_del();
                        limit_backward = c16;
                    }
                lab14: ;
                    cursor = limit - c10;
                    {
                        int c17 = limit - cursor;
                        ket = cursor;
                        if (!(eq_s_b("i")))
                        {
                            goto lab20;
                        }
                        bra = cursor;
                        {
                            int c18 = limit - cursor;
                            if (!(eq_s_b("c")))
                            {
                                goto lab20;
                            }
                            cursor = limit - c18;
                        }
                        if (I_pV > cursor)
                        {
                            goto lab20;
                        }
                        slice_del();
                    lab20: ;
                        cursor = limit - c17;
                    }
                    goto lab12;
                lab13: ;
                    cursor = limit - c9;
                    ket = cursor;
                    if (find_among_b(a_7, null) == 0)
                    {
                        goto lab11;
                    }
                    bra = cursor;
                    if (I_pV > cursor)
                    {
                        goto lab11;
                    }
                    slice_del();
                }
            lab12: ;
            lab11: ;
                cursor = limit - c8;
            }
            {
                int c19 = limit - cursor;
                ket = cursor;
                among_var = find_among_b(a_8, null);
                if (among_var == 0)
                {
                    goto lab21;
                }
                bra = cursor;
                switch (among_var) {
                    case 1: {
                        if (I_pV > cursor)
                        {
                            goto lab21;
                        }
                        slice_del();
                        ket = cursor;
                        {
                            int c20 = limit - cursor;
                            if (!(eq_s_b("u")))
                            {
                                goto lab23;
                            }
                            bra = cursor;
                            {
                                int c21 = limit - cursor;
                                if (!(eq_s_b("g")))
                                {
                                    goto lab23;
                                }
                                cursor = limit - c21;
                            }
                            goto lab22;
                        lab23: ;
                            cursor = limit - c20;
                            if (!(eq_s_b("i")))
                            {
                                goto lab21;
                            }
                            bra = cursor;
                            {
                                int c22 = limit - cursor;
                                if (!(eq_s_b("c")))
                                {
                                    goto lab21;
                                }
                                cursor = limit - c22;
                            }
                        }
                    lab22: ;
                        if (I_pV > cursor)
                        {
                            goto lab21;
                        }
                        slice_del();
                        break;
                    }
                    case 2: {
                        slice_from("c");
                        break;
                    }
                }
            lab21: ;
                cursor = limit - c19;
            }
            cursor = limit_backward;
            {
                int c23 = cursor;
                while (true)
                {
                    int c24 = cursor;
                    bra = cursor;
                    among_var = find_among(a_1, null);
                    ket = cursor;
                    switch (among_var) {
                        case 1: {
                            slice_from("ã");
                            break;
                        }
                        case 2: {
                            slice_from("õ");
                            break;
                        }
                        case 3: {
                            if (cursor >= limit)
                            {
                                goto lab25;
                            }
                            cursor++;
                            break;
                        }
                    }
                    continue;
                lab25: ;
                    cursor = c24;
                    break;
                }
                cursor = c23;
            }
            return true;
        }

    }
}

