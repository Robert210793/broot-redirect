import * as React from 'react';
import { Dropdown, IDropdownOption } from '@fluentui/react';
import { REDIRECT_RULE_STATUSES } from '../logic/RedirectRuleStatus';
import { RedirectRuleStatus } from '../types';

export interface IStatusDropdownProps {
  label?: string;
  ariaLabel: string;
  selected: RedirectRuleStatus | undefined;
  placeholder?: string;
  disabled: boolean;
  className?: string;
  onChange: (status: RedirectRuleStatus) => void;
}

const OPTIONS: IDropdownOption[] = REDIRECT_RULE_STATUSES.map((status) => ({ key: status, text: status }));

export const StatusDropdown: React.FC<IStatusDropdownProps> = (props) => (
  <Dropdown
    label={props.label}
    ariaLabel={props.ariaLabel}
    options={OPTIONS}
    selectedKey={props.selected !== undefined ? props.selected : null}
    placeholder={props.placeholder}
    disabled={props.disabled}
    className={props.className}
    onChange={(_, option) => {
      if (option) {
        props.onChange(option.key as RedirectRuleStatus);
      }
    }}
  />
);
