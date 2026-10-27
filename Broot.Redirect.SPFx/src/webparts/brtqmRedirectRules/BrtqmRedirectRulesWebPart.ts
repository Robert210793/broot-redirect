import * as React from 'react';
import * as ReactDom from 'react-dom';
import { Version } from '@microsoft/sp-core-library';
import { IPropertyPaneConfiguration } from '@microsoft/sp-property-pane';
import { BaseClientSideWebPart } from '@microsoft/sp-webpart-base';
import { spfi } from '@pnp/sp';
import { SPFx } from '@pnp/sp/behaviors/spfx';
import * as strings from 'BrtqmRedirectRulesWebPartStrings';
import { IRedirectRulesEditorProps, RedirectRulesEditor } from './components/RedirectRulesEditor';
import { AreaSaveCoordinator } from './services/AreaSaveCoordinator';
import { listUrlForSite, RedirectRulesService } from './services/RedirectRulesService';

export interface IBrtqmRedirectRulesWebPartProps {}

export default class BrtqmRedirectRulesWebPart extends BaseClientSideWebPart<IBrtqmRedirectRulesWebPartProps> {
  private service!: RedirectRulesService;
  private coordinator!: AreaSaveCoordinator;

  protected async onInit(): Promise<void> {
    await super.onInit();

    const site = this.context.pageContext.site;
    const sp = spfi(site.absoluteUrl).using(SPFx(this.context));

    this.service = new RedirectRulesService(sp, listUrlForSite(site.serverRelativeUrl));
    this.coordinator = new AreaSaveCoordinator(this.service, new URL(site.absoluteUrl).origin);
  }

  public render(): void {
    const element: React.ReactElement<IRedirectRulesEditorProps> = React.createElement(RedirectRulesEditor, {
      service: this.service,
      coordinator: this.coordinator
    });

    ReactDom.render(element, this.domElement);
  }

  protected onDispose(): void {
    ReactDom.unmountComponentAtNode(this.domElement);
  }

  protected get dataVersion(): Version {
    return Version.parse('1.0');
  }

  protected getPropertyPaneConfiguration(): IPropertyPaneConfiguration {
    return {
      pages: [
        {
          header: { description: strings.PropertyPaneDescription },
          groups: []
        }
      ]
    };
  }
}
