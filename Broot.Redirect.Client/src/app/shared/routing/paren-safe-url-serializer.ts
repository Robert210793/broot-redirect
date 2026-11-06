import { DefaultUrlSerializer, UrlTree } from '@angular/router';

export class ParenSafeUrlSerializer extends DefaultUrlSerializer {
    override parse(url: string): UrlTree {
        const fragmentIndex = url.indexOf('#');
        const fragment = fragmentIndex >= 0 ? url.slice(fragmentIndex) : '';
        const withoutFragment = fragmentIndex >= 0 ? url.slice(0, fragmentIndex) : url;

        const queryIndex = withoutFragment.indexOf('?');
        const query = queryIndex >= 0 ? withoutFragment.slice(queryIndex) : '';
        const path = queryIndex >= 0 ? withoutFragment.slice(0, queryIndex) : withoutFragment;

        const escapedPath = path.replace(/\(/g, '%28').replace(/\)/g, '%29');

        return super.parse(escapedPath + query + fragment);
    }
}
