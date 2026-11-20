// Generated from italian.sbl by Snowball 3.2.0 - https://snowballstem.org/

#pragma warning disable 0164
#pragma warning disable 0162

namespace Snowball
{
    using System;
    using System.Text;

    ///<summary>
    ///  This class implements the stemming algorithm defined by a snowball script.
    ///  Generated from italian.sbl by Snowball 3.2.0 - https://snowballstem.org/
    ///</summary>
    ///
    [System.CodeDom.Compiler.GeneratedCode("Snowball", "3.2.0")]
    public partial class ItalianStemmer : Stemmer
    {
        private const string g_v = "aeiouàèìòù";
        private const string g_AEIO = "aeioàèìò";
        private const string g_CG = "cg";

        private static readonly Among[] a_0 = new[]
        {
            new Among("all'", -1, -1, 0),
            new Among("d'", -1, -1, 0),
            new Among("dall'", -1, -1, 0),
            new Among("dell'", -1, -1, 0),
            new Among("gl'", -1, -1, 0),
            new Among("l'", -1, -1, 0),
            new Among("m'", -1, -1, 0),
            new Among("nell'", -1, -1, 0),
            new Among("quell'", -1, -1, 0),
            new Among("quest'", -1, -1, 0),
            new Among("s'", -1, -1, 0),
            new Among("sull'", -1, -1, 0),
            new Among("t'", -1, -1, 0),
            new Among("tutt'", -1, -1, 0),
            new Among("un'", -1, -1, 0),
            new Among("v'", -1, -1, 0)
        };

        private static readonly Among[] a_1 = new[]
        {
            new Among("", -1, 7, 0),
            new Among("qu", 0, 6, 0),
            new Among("á", 0, 1, 0),
            new Among("é", 0, 2, 0),
            new Among("í", 0, 3, 0),
            new Among("ó", 0, 4, 0),
            new Among("ú", 0, 5, 0)
        };

        private static readonly Among[] a_2 = new[]
        {
            new Among("", -1, 3, 0),
            new Among("I", 0, 1, 0),
            new Among("U", 0, 2, 0)
        };

        private static readonly Among[] a_3 = new[]
        {
            new Among("la", -1, -1, 0),
            new Among("cela", 0, -1, 0),
            new Among("gliela", 0, -1, 0),
            new Among("mela", 0, -1, 0),
            new Among("tela", 0, -1, 0),
            new Among("vela", 0, -1, 0),
            new Among("le", -1, -1, 0),
            new Among("cele", 6, -1, 0),
            new Among("gliele", 6, -1, 0),
            new Among("mele", 6, -1, 0),
            new Among("tele", 6, -1, 0),
            new Among("vele", 6, -1, 0),
            new Among("ne", -1, -1, 0),
            new Among("cene", 12, -1, 0),
            new Among("gliene", 12, -1, 0),
            new Among("mene", 12, -1, 0),
            new Among("sene", 12, -1, 0),
            new Among("tene", 12, -1, 0),
            new Among("vene", 12, -1, 0),
            new Among("ci", -1, -1, 0),
            new Among("li", -1, -1, 0),
            new Among("celi", 20, -1, 0),
            new Among("glieli", 20, -1, 0),
            new Among("meli", 20, -1, 0),
            new Among("teli", 20, -1, 0),
            new Among("veli", 20, -1, 0),
            new Among("gli", 20, -1, 0),
            new Among("mi", -1, -1, 0),
            new Among("si", -1, -1, 0),
            new Among("ti", -1, -1, 0),
            new Among("vi", -1, -1, 0),
            new Among("lo", -1, -1, 0),
            new Among("celo", 31, -1, 0),
            new Among("glielo", 31, -1, 0),
            new Among("melo", 31, -1, 0),
            new Among("telo", 31, -1, 0),
            new Among("velo", 31, -1, 0)
        };

        private static readonly Among[] a_4 = new[]
        {
            new Among("ando", -1, 1, 0),
            new Among("endo", -1, 1, 0),
            new Among("ar", -1, 2, 0),
            new Among("er", -1, 2, 0),
            new Among("ir", -1, 2, 0)
        };

        private static readonly Among[] a_5 = new[]
        {
            new Among("ic", -1, -1, 0),
            new Among("abil", -1, -1, 0),
            new Among("os", -1, -1, 0),
            new Among("iv", -1, 1, 0)
        };

        private static readonly Among[] a_6 = new[]
        {
            new Among("ic", -1, 1, 0),
            new Among("abil", -1, 1, 0),
            new Among("iv", -1, 1, 0)
        };

        private static readonly Among[] a_7 = new[]
        {
            new Among("ica", -1, 1, 0),
            new Among("logia", -1, 3, 0),
            new Among("osa", -1, 1, 0),
            new Among("ista", -1, 1, 0),
            new Among("iva", -1, 9, 0),
            new Among("anza", -1, 1, 0),
            new Among("enza", -1, 5, 0),
            new Among("ice", -1, 1, 0),
            new Among("atrice", 7, 1, 0),
            new Among("iche", -1, 1, 0),
            new Among("logie", -1, 3, 0),
            new Among("abile", -1, 1, 0),
            new Among("ibile", -1, 1, 0),
            new Among("usione", -1, 4, 0),
            new Among("azione", -1, 2, 0),
            new Among("uzione", -1, 4, 0),
            new Among("atore", -1, 2, 0),
            new Among("ose", -1, 1, 0),
            new Among("ante", -1, 1, 0),
            new Among("mente", -1, 1, 0),
            new Among("amente", 19, 7, 0),
            new Among("iste", -1, 1, 0),
            new Among("ive", -1, 9, 0),
            new Among("anze", -1, 1, 0),
            new Among("enze", -1, 5, 0),
            new Among("ici", -1, 1, 0),
            new Among("atrici", 25, 1, 0),
            new Among("ichi", -1, 1, 0),
            new Among("abili", -1, 1, 0),
            new Among("ibili", -1, 1, 0),
            new Among("ismi", -1, 1, 0),
            new Among("usioni", -1, 4, 0),
            new Among("azioni", -1, 2, 0),
            new Among("uzioni", -1, 4, 0),
            new Among("atori", -1, 2, 0),
            new Among("osi", -1, 1, 0),
            new Among("anti", -1, 1, 0),
            new Among("amenti", -1, 6, 0),
            new Among("imenti", -1, 6, 0),
            new Among("isti", -1, 1, 0),
            new Among("ivi", -1, 9, 0),
            new Among("ico", -1, 1, 0),
            new Among("ismo", -1, 1, 0),
            new Among("oso", -1, 1, 0),
            new Among("amento", -1, 6, 0),
            new Among("imento", -1, 6, 0),
            new Among("ivo", -1, 9, 0),
            new Among("ità", -1, 8, 0),
            new Among("istà", -1, 1, 0),
            new Among("istè", -1, 1, 0),
            new Among("istì", -1, 1, 0)
        };

        private static readonly Among[] a_8 = new[]
        {
            new Among("isca", -1, 1, 0),
            new Among("enda", -1, 1, 0),
            new Among("ata", -1, 1, 0),
            new Among("ita", -1, 1, 0),
            new Among("uta", -1, 1, 0),
            new Among("ava", -1, 1, 0),
            new Among("eva", -1, 1, 0),
            new Among("iva", -1, 1, 0),
            new Among("erebbe", -1, 1, 0),
            new Among("irebbe", -1, 1, 0),
            new Among("isce", -1, 1, 0),
            new Among("ende", -1, 1, 0),
            new Among("are", -1, 1, 0),
            new Among("ere", -1, 1, 0),
            new Among("ire", -1, 1, 0),
            new Among("asse", -1, 1, 0),
            new Among("ate", -1, 1, 0),
            new Among("avate", 16, 1, 0),
            new Among("evate", 16, 1, 0),
            new Among("ivate", 16, 1, 0),
            new Among("ete", -1, 1, 0),
            new Among("erete", 20, 1, 0),
            new Among("irete", 20, 1, 0),
            new Among("ite", -1, 1, 0),
            new Among("ereste", -1, 1, 0),
            new Among("ireste", -1, 1, 0),
            new Among("ute", -1, 1, 0),
            new Among("erai", -1, 1, 0),
            new Among("irai", -1, 1, 0),
            new Among("isci", -1, 1, 0),
            new Among("endi", -1, 1, 0),
            new Among("erei", -1, 1, 0),
            new Among("irei", -1, 1, 0),
            new Among("assi", -1, 1, 0),
            new Among("ati", -1, 1, 0),
            new Among("iti", -1, 1, 0),
            new Among("eresti", -1, 1, 0),
            new Among("iresti", -1, 1, 0),
            new Among("uti", -1, 1, 0),
            new Among("avi", -1, 1, 0),
            new Among("evi", -1, 1, 0),
            new Among("ivi", -1, 1, 0),
            new Among("isco", -1, 1, 0),
            new Among("ando", -1, 1, 0),
            new Among("endo", -1, 1, 0),
            new Among("Yamo", -1, 1, 0),
            new Among("iamo", -1, 1, 0),
            new Among("avamo", -1, 1, 0),
            new Among("evamo", -1, 1, 0),
            new Among("ivamo", -1, 1, 0),
            new Among("eremo", -1, 1, 0),
            new Among("iremo", -1, 1, 0),
            new Among("assimo", -1, 1, 0),
            new Among("ammo", -1, 1, 0),
            new Among("emmo", -1, 1, 0),
            new Among("eremmo", 54, 1, 0),
            new Among("iremmo", 54, 1, 0),
            new Among("immo", -1, 1, 0),
            new Among("ano", -1, 1, 0),
            new Among("iscano", 58, 1, 0),
            new Among("avano", 58, 1, 0),
            new Among("evano", 58, 1, 0),
            new Among("ivano", 58, 1, 0),
            new Among("eranno", -1, 1, 0),
            new Among("iranno", -1, 1, 0),
            new Among("ono", -1, 1, 0),
            new Among("iscono", 65, 1, 0),
            new Among("arono", 65, 1, 0),
            new Among("erono", 65, 1, 0),
            new Among("irono", 65, 1, 0),
            new Among("erebbero", -1, 1, 0),
            new Among("irebbero", -1, 1, 0),
            new Among("assero", -1, 1, 0),
            new Among("essero", -1, 1, 0),
            new Among("issero", -1, 1, 0),
            new Among("ato", -1, 1, 0),
            new Among("ito", -1, 1, 0),
            new Among("uto", -1, 1, 0),
            new Among("avo", -1, 1, 0),
            new Among("evo", -1, 1, 0),
            new Among("ivo", -1, 1, 0),
            new Among("ar", -1, 1, 0),
            new Among("ir", -1, 1, 0),
            new Among("erà", -1, 1, 0),
            new Among("irà", -1, 1, 0),
            new Among("erò", -1, 1, 0),
            new Among("irò", -1, 1, 0)
        };


        protected override bool stem()
        {
            int among_var;
            int I_p2;
            int I_p1;
            int I_pV;
            {
                int c1 = cursor;
                bra = cursor;
                if (find_among(a_0, null) == 0)
                {
                    goto lab0;
                }
                ket = cursor;
                if (cursor >= limit)
                {
                    goto lab0;
                }
                slice_del();
            lab0: ;
                cursor = c1;
            }
            {
                int c2 = cursor;
                {
                    int c3 = cursor;
                    while (true)
                    {
                        int c4 = cursor;
                        bra = cursor;
                        among_var = find_among(a_1, null);
                        ket = cursor;
                        switch (among_var) {
                            case 1: {
                                slice_from("à");
                                break;
                            }
                            case 2: {
                                slice_from("è");
                                break;
                            }
                            case 3: {
                                slice_from("ì");
                                break;
                            }
                            case 4: {
                                slice_from("ò");
                                break;
                            }
                            case 5: {
                                slice_from("ù");
                                break;
                            }
                            case 6: {
                                slice_from("qU");
                                break;
                            }
                            case 7: {
                                if (cursor >= limit)
                                {
                                    goto lab2;
                                }
                                cursor++;
                                break;
                            }
                        }
                        continue;
                    lab2: ;
                        cursor = c4;
                        break;
                    }
                    cursor = c3;
                }
                while (true)
                {
                    int c5 = cursor;
                    while (true)
                    {
                        int c6 = cursor;
                        if (in_grouping(g_v, 97, 249, false) != 0)
                        {
                            goto lab4;
                        }
                        bra = cursor;
                        {
                            int c7 = cursor;
                            if (!(eq_s("u")))
                            {
                                goto lab6;
                            }
                            ket = cursor;
                            if (in_grouping(g_v, 97, 249, false) != 0)
                            {
                                goto lab6;
                            }
                            slice_from("U");
                            goto lab5;
                        lab6: ;
                            cursor = c7;
                            if (!(eq_s("i")))
                            {
                                goto lab4;
                            }
                            ket = cursor;
                            if (in_grouping(g_v, 97, 249, false) != 0)
                            {
                                goto lab4;
                            }
                            slice_from("I");
                        }
                    lab5: ;
                        cursor = c6;
                        break;
                    lab4: ;
                        cursor = c6;
                        if (cursor >= limit)
                        {
                            goto lab3;
                        }
                        cursor++;
                    }
                    continue;
                lab3: ;
                    cursor = c5;
                    break;
                }
                cursor = c2;
            }
            I_pV = limit;
            I_p1 = limit;
            I_p2 = limit;
            {
                int c8 = cursor;
                {
                    int c9 = cursor;
                    if (in_grouping(g_v, 97, 249, false) != 0)
                    {
                        goto lab10;
                    }
                    {
                        int c10 = cursor;
                        if (out_grouping(g_v, 97, 249, false) != 0)
                        {
                            goto lab12;
                        }
                        {

                            int ret = out_grouping(g_v, 97, 249, true);
                            if (ret < 0)
                            {
                                goto lab12;
                            }

                            cursor += ret;
                        }
                        goto lab11;
                    lab12: ;
                        cursor = c10;
                        if (in_grouping(g_v, 97, 249, false) != 0)
                        {
                            goto lab10;
                        }
                        {

                            int ret = in_grouping(g_v, 97, 249, true);
                            if (ret < 0)
                            {
                                goto lab10;
                            }

                            cursor += ret;
                        }
                    }
                lab11: ;
                    goto lab9;
                lab10: ;
                    cursor = c9;
                    if (!(eq_s("divan")))
                    {
                        goto lab13;
                    }
                    goto lab9;
                lab13: ;
                    cursor = c9;
                    if (out_grouping(g_v, 97, 249, false) != 0)
                    {
                        goto lab8;
                    }
                    {
                        int c11 = cursor;
                        if (out_grouping(g_v, 97, 249, false) != 0)
                        {
                            goto lab15;
                        }
                        {

                            int ret = out_grouping(g_v, 97, 249, true);
                            if (ret < 0)
                            {
                                goto lab15;
                            }

                            cursor += ret;
                        }
                        goto lab14;
                    lab15: ;
                        cursor = c11;
                        if (in_grouping(g_v, 97, 249, false) != 0)
                        {
                            goto lab8;
                        }
                        if (cursor >= limit)
                        {
                            goto lab8;
                        }
                        cursor++;
                    }
                lab14: ;
                }
            lab9: ;
                I_pV = cursor;
            lab8: ;
                cursor = c8;
            }
            {
                int c12 = cursor;
                {

                    int ret = out_grouping(g_v, 97, 249, true);
                    if (ret < 0)
                    {
                        goto lab16;
                    }

                    cursor += ret;
                }
                {

                    int ret = in_grouping(g_v, 97, 249, true);
                    if (ret < 0)
                    {
                        goto lab16;
                    }

                    cursor += ret;
                }
                I_p1 = cursor;
                {

                    int ret = out_grouping(g_v, 97, 249, true);
                    if (ret < 0)
                    {
                        goto lab16;
                    }

                    cursor += ret;
                }
                {

                    int ret = in_grouping(g_v, 97, 249, true);
                    if (ret < 0)
                    {
                        goto lab16;
                    }

                    cursor += ret;
                }
                I_p2 = cursor;
            lab16: ;
                cursor = c12;
            }
            limit_backward = cursor;
            cursor = limit;
            {
                int c13 = limit - cursor;
                ket = cursor;
                if (find_among_b(a_3, null) == 0)
                {
                    goto lab17;
                }
                bra = cursor;
                among_var = find_among_b(a_4, null);
                if (among_var == 0)
                {
                    goto lab17;
                }
                if (I_pV > cursor)
                {
                    goto lab17;
                }
                switch (among_var) {
                    case 1: {
                        slice_del();
                        break;
                    }
                    case 2: {
                        slice_from("e");
                        break;
                    }
                }
            lab17: ;
                cursor = limit - c13;
            }
            {
                int c14 = limit - cursor;
                {
                    int c15 = limit - cursor;
                    ket = cursor;
                    among_var = find_among_b(a_7, null);
                    if (among_var == 0)
                    {
                        goto lab20;
                    }
                    bra = cursor;
                    switch (among_var) {
                        case 1: {
                            if (I_p2 > cursor)
                            {
                                goto lab20;
                            }
                            slice_del();
                            break;
                        }
                        case 2: {
                            if (I_p2 > cursor)
                            {
                                goto lab20;
                            }
                            slice_del();
                            {
                                int c16 = limit - cursor;
                                ket = cursor;
                                if (!(eq_s_b("ic")))
                                {
                                    {
                                        cursor = limit - c16;
                                        goto lab21;
                                    }
                                }
                                bra = cursor;
                                if (I_p2 > cursor)
                                {
                                    {
                                        cursor = limit - c16;
                                        goto lab21;
                                    }
                                }
                                slice_del();
                            lab21: ;
                            }
                            break;
                        }
                        case 3: {
                            if (I_p2 > cursor)
                            {
                                goto lab20;
                            }
                            slice_from("log");
                            break;
                        }
                        case 4: {
                            if (I_p2 > cursor)
                            {
                                goto lab20;
                            }
                            slice_from("u");
                            break;
                        }
                        case 5: {
                            if (I_p2 > cursor)
                            {
                                goto lab20;
                            }
                            slice_from("ente");
                            break;
                        }
                        case 6: {
                            if (I_pV > cursor)
                            {
                                goto lab20;
                            }
                            slice_del();
                            break;
                        }
                        case 7: {
                            if (I_p1 > cursor)
                            {
                                goto lab20;
                            }
                            slice_del();
                            {
                                int c17 = limit - cursor;
                                ket = cursor;
                                among_var = find_among_b(a_5, null);
                                if (among_var == 0)
                                {
                                    {
                                        cursor = limit - c17;
                                        goto lab22;
                                    }
                                }
                                bra = cursor;
                                if (I_p2 > cursor)
                                {
                                    {
                                        cursor = limit - c17;
                                        goto lab22;
                                    }
                                }
                                slice_del();
                                switch (among_var) {
                                    case 1: {
                                        ket = cursor;
                                        if (!(eq_s_b("at")))
                                        {
                                            {
                                                cursor = limit - c17;
                                                goto lab22;
                                            }
                                        }
                                        bra = cursor;
                                        if (I_p2 > cursor)
                                        {
                                            {
                                                cursor = limit - c17;
                                                goto lab22;
                                            }
                                        }
                                        slice_del();
                                        break;
                                    }
                                }
                            lab22: ;
                            }
                            break;
                        }
                        case 8: {
                            if (I_p2 > cursor)
                            {
                                goto lab20;
                            }
                            slice_del();
                            {
                                int c18 = limit - cursor;
                                ket = cursor;
                                if (find_among_b(a_6, null) == 0)
                                {
                                    {
                                        cursor = limit - c18;
                                        goto lab23;
                                    }
                                }
                                bra = cursor;
                                if (I_p2 > cursor)
                                {
                                    {
                                        cursor = limit - c18;
                                        goto lab23;
                                    }
                                }
                                slice_del();
                            lab23: ;
                            }
                            break;
                        }
                        case 9: {
                            if (I_p2 > cursor)
                            {
                                goto lab20;
                            }
                            slice_del();
                            {
                                int c19 = limit - cursor;
                                ket = cursor;
                                if (!(eq_s_b("at")))
                                {
                                    {
                                        cursor = limit - c19;
                                        goto lab24;
                                    }
                                }
                                bra = cursor;
                                if (I_p2 > cursor)
                                {
                                    {
                                        cursor = limit - c19;
                                        goto lab24;
                                    }
                                }
                                slice_del();
                                ket = cursor;
                                if (!(eq_s_b("ic")))
                                {
                                    {
                                        cursor = limit - c19;
                                        goto lab24;
                                    }
                                }
                                bra = cursor;
                                if (I_p2 > cursor)
                                {
                                    {
                                        cursor = limit - c19;
                                        goto lab24;
                                    }
                                }
                                slice_del();
                            lab24: ;
                            }
                            break;
                        }
                    }
                    goto lab19;
                lab20: ;
                    cursor = limit - c15;
                    if (cursor < I_pV)
                    {
                        goto lab18;
                    }
                    int c20 = limit_backward;
                    limit_backward = I_pV;
                    ket = cursor;
                    if (find_among_b(a_8, null) == 0)
                    {
                        {
                            limit_backward = c20;
                            goto lab18;
                        }
                    }
                    bra = cursor;
                    slice_del();
                    limit_backward = c20;
                }
            lab19: ;
            lab18: ;
                cursor = limit - c14;
            }
            {
                int c21 = limit - cursor;
                {
                    int c22 = limit - cursor;
                    ket = cursor;
                    if (in_grouping_b(g_AEIO, 97, 242, false) != 0)
                    {
                        {
                            cursor = limit - c22;
                            goto lab26;
                        }
                    }
                    bra = cursor;
                    if (I_pV > cursor)
                    {
                        {
                            cursor = limit - c22;
                            goto lab26;
                        }
                    }
                    slice_del();
                    ket = cursor;
                    if (!(eq_s_b("i")))
                    {
                        {
                            cursor = limit - c22;
                            goto lab26;
                        }
                    }
                    bra = cursor;
                    if (I_pV > cursor)
                    {
                        {
                            cursor = limit - c22;
                            goto lab26;
                        }
                    }
                    slice_del();
                lab26: ;
                }
                {
                    int c23 = limit - cursor;
                    ket = cursor;
                    if (!(eq_s_b("h")))
                    {
                        {
                            cursor = limit - c23;
                            goto lab27;
                        }
                    }
                    bra = cursor;
                    if (in_grouping_b(g_CG, 99, 103, false) != 0)
                    {
                        {
                            cursor = limit - c23;
                            goto lab27;
                        }
                    }
                    if (I_pV > cursor)
                    {
                        {
                            cursor = limit - c23;
                            goto lab27;
                        }
                    }
                    slice_del();
                lab27: ;
                }
                cursor = limit - c21;
            }
            cursor = limit_backward;
            {
                int c24 = cursor;
                while (true)
                {
                    int c25 = cursor;
                    bra = cursor;
                    among_var = find_among(a_2, null);
                    ket = cursor;
                    switch (among_var) {
                        case 1: {
                            slice_from("i");
                            break;
                        }
                        case 2: {
                            slice_from("u");
                            break;
                        }
                        case 3: {
                            if (cursor >= limit)
                            {
                                goto lab29;
                            }
                            cursor++;
                            break;
                        }
                    }
                    continue;
                lab29: ;
                    cursor = c25;
                    break;
                }
                cursor = c24;
            }
            return true;
        }

    }
}

