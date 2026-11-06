import { DefaultUrlSerializer } from '@angular/router';
import { ParenSafeUrlSerializer } from './paren-safe-url-serializer';

describe('ParenSafeUrlSerializer', () => {
    let sut: ParenSafeUrlSerializer;

    beforeEach(() => {
        sut = new ParenSafeUrlSerializer();
    });

    function segmentsOf(url: string): string[] {
        return sut.parse(url).root.children['primary'].segments.map((segment) => segment.path);
    }

    it('keeps a decoded parenthesis inside the segment instead of opening an outlet', () => {
        const segments = segmentsOf('/ims/Strategie/11 IMS (Integr. Mgmt.system)/x.docx');

        expect(segments).toEqual(['ims', 'Strategie', '11 IMS (Integr. Mgmt.system)', 'x.docx']);
    });

    it('keeps an encoded parenthesis inside the segment', () => {
        const segments = segmentsOf('/ims/Strategie/11%20IMS%20%28Integr.%29/x.docx');

        expect(segments).toEqual(['ims', 'Strategie', '11 IMS (Integr.)', 'x.docx']);
    });

    it('does not create an auxiliary outlet for a parenthesised segment', () => {
        const tree = sut.parse('/ims/11 IMS (Integr. Mgmt.system)/x.docx');

        expect(Object.keys(tree.root.children)).toEqual(['primary']);
    });

    it('preserves the query string', () => {
        const tree = sut.parse('/ims/A (B)/x.aspx?RootFolder=%2Fims%2FA&View=1');

        expect(tree.queryParams['RootFolder']).toBe('/ims/A');
        expect(tree.queryParams['View']).toBe('1');
    });

    it('preserves the fragment', () => {
        const tree = sut.parse('/ims/A (B)/x.aspx#Auditmanagement');

        expect(tree.fragment).toBe('Auditmanagement');
    });

    it('handles umlauts and spaces alongside parentheses', () => {
        const segments = segmentsOf('/ims/Prüfung (2026)/Auditorenübersicht.pdf');

        expect(segments).toEqual(['ims', 'Prüfung (2026)', 'Auditorenübersicht.pdf']);
    });

    it('parses urls without parentheses exactly like the default serializer', () => {
        const url = '/rules?page=2#top';

        expect(sut.serialize(sut.parse(url))).toBe(new DefaultUrlSerializer().serialize(new DefaultUrlSerializer().parse(url)));
    });

    it('still routes the admin paths', () => {
        expect(segmentsOf('/rules')).toEqual(['rules']);
        expect(segmentsOf('/settings')).toEqual(['settings']);
    });
});
