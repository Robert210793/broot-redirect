import * as React from 'react';
import { Dropdown, IDropdownOption } from '@fluentui/react';
import * as strings from 'BrtqmRedirectRulesWebPartStrings';
import { AreaOption } from '../types';

export const NEW_AREA_KEY: string = '__new__';

export interface IAreaPickerProps {
  areas: AreaOption[];
  selectedKey?: string;
  disabled: boolean;
  onSelect: (key: string) => void;
}

export const AreaPicker: React.FC<IAreaPickerProps> = (props) => {
  const options: IDropdownOption[] = React.useMemo(
    () => [{ key: NEW_AREA_KEY, text: strings.NewAreaOption } as IDropdownOption].concat(
      props.areas.map((area) => ({ key: area.key, text: area.name.trim() }))
    ),
    [props.areas]
  );

  return (
    <Dropdown
      label={strings.AreaLabel}
      placeholder={strings.AreaPlaceholder}
      options={options}
      selectedKey={props.selectedKey !== undefined ? props.selectedKey : null}
      disabled={props.disabled}
      onChange={(_, option) => option && props.onSelect(String(option.key))}
    />
  );
};
