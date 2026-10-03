#include "nauty.h"
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#define OM_EXPORT __declspec(dllexport)
#else
#define OM_EXPORT __attribute__((visibility("default")))
#endif

/*
 * Build a vertex-coloured auxiliary graph for an ordered complete graph.
 *
 * Vertices 0..m-1 are the ordered original vertices and form a path.
 * For every original edge {u,v}, one degree-2 gadget vertex is added,
 * joined to u and v.  Gadget vertices are in the red/blue colour class
 * according to the original edge colour.
 *
 * The initial partition is therefore:
 *   original vertices | red gadgets | blue gadgets.
 *
 * densenauty() returns a canonical labelling for this coloured graph.
 * We do NOT interpret lab[] as an ordering of the original path vertices.
 * A canonical labelling is allowed to interleave the three colour classes.
 * Instead, this function serializes the entire canonical graph together
 * with the canonical colour of every vertex.  That certificate is invariant
 * under all colour-preserving isomorphisms, including reversal of the order.
 */
OM_EXPORT int
GetCanonicalCertificate(int vertexCount,
                        const unsigned char *edgeColors,
                        unsigned char *certificate,
                        int certificateCapacity,
                        int *certificateLength,
                        int *automorphismOrder)
{
    if (vertexCount < 2 || edgeColors == NULL || certificate == NULL || certificateLength == NULL)
        return 1;

    const int edgeCount = vertexCount * (vertexCount - 1) / 2;
    const int totalVertices = vertexCount + edgeCount;
    const int words = SETWORDSNEEDED(totalVertices);
    const int adjacencyBytes = (totalVertices * (totalVertices - 1) / 2 + 7) / 8;
    const int required = totalVertices + adjacencyBytes;

    if (certificateCapacity < required)
        return 4;

    nauty_check(WORDSIZE, words, totalVertices, NAUTYVERSIONID);

    graph *g = (graph *)calloc((size_t)words * totalVertices, sizeof(graph));
    graph *canong = (graph *)calloc((size_t)words * totalVertices, sizeof(graph));
    int *lab = (int *)malloc((size_t)totalVertices * sizeof(int));
    int *ptn = (int *)malloc((size_t)totalVertices * sizeof(int));
    int *orbits = (int *)malloc((size_t)totalVertices * sizeof(int));
    if (!g || !canong || !lab || !ptn || !orbits)
    {
        free(g); free(canong); free(lab); free(ptn); free(orbits);
        return 2;
    }

    EMPTYGRAPH(g, words, totalVertices);

    /* The order path. */
    for (int i = 0; i + 1 < vertexCount; ++i)
        ADDONEEDGE(g, i, i + 1, words);

    /* One gadget vertex for each original coloured edge. */
    int e = 0;
    for (int u = 0; u < vertexCount; ++u)
    {
        for (int v = u + 1; v < vertexCount; ++v, ++e)
        {
            const int x = vertexCount + e;
            ADDONEEDGE(g, x, u, words);
            ADDONEEDGE(g, x, v, words);
        }
    }

    /* Initial partition: original | red gadgets | blue gadgets. */
    int k = 0;
    for (int i = 0; i < vertexCount; ++i)
        lab[k++] = i;

    for (int color = 1; color >= 0; --color)
    {
        for (e = 0; e < edgeCount; ++e)
        {
            const int isRed = (edgeColors[e] != 0);
            if (isRed == (color != 0))
                lab[k++] = vertexCount + e;
        }
    }

    for (int i = 0; i < totalVertices; ++i)
        ptn[i] = 1;
    ptn[vertexCount - 1] = 0;

    k = vertexCount;
    for (int color = 1; color >= 0; --color)
    {
        const int first = k;
        while (k < totalVertices)
        {
            const int old = lab[k] - vertexCount;
            if (old < 0) break;
            if (((edgeColors[old] != 0) != (color != 0))) break;
            ++k;
        }
        if (first < k)
            ptn[k - 1] = 0;
    }

    DEFAULTOPTIONS_GRAPH(options);
    statsblk stats;
    memset(&stats, 0, sizeof(stats));
    options.getcanon = TRUE;
    options.defaultptn = FALSE;
    options.digraph = FALSE;

    densenauty(g, lab, ptn, orbits, &options, &stats,
               words, totalVertices, canong);

    /* Canonical vertex colours, one byte per canonical vertex. */
    int out = 0;
    for (int pos = 0; pos < totalVertices; ++pos)
    {
        const int old = lab[pos];
        if (old < 0 || old >= totalVertices)
        {
            free(g); free(canong); free(lab); free(ptn); free(orbits);
            return 3;
        }

        if (old < vertexCount)
            certificate[out++] = 0;              /* original/order vertex */
        else
            certificate[out++] = (unsigned char)(edgeColors[old - vertexCount] ? 1 : 2);
    }

    /* Canonical adjacency, upper triangle, packed LSB-first. */
    int bitIndex = 0;
    for (int u = 0; u < totalVertices; ++u)
    {
        for (int v = u + 1; v < totalVertices; ++v, ++bitIndex)
        {
            if (ISELEMENT(GRAPHROW(canong, u, words), v))
                certificate[out + (bitIndex >> 3)] |= (unsigned char)(1u << (bitIndex & 7));
        }
    }
    /* Explicitly clear any unused tail bits in the final byte. */
    if (adjacencyBytes > 0)
    {
        const int usedBits = totalVertices * (totalVertices - 1) / 2;
        const int tail = usedBits & 7;
        if (tail != 0)
            certificate[out + adjacencyBytes - 1] &= (unsigned char)((1u << tail) - 1u);
    }

    *certificateLength = required;

    if (automorphismOrder != NULL)
    {
        double groupSize = stats.grpsize1;
        for (int i = 0; i < stats.grpsize2; ++i) groupSize *= 10.0;
        if (groupSize > 2147483647.0)
            *automorphismOrder = -1;
        else
            *automorphismOrder = (int)(groupSize + 0.5);
    }

    free(g); free(canong); free(lab); free(ptn); free(orbits);
    return 0;
}
